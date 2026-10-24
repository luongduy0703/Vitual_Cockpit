#!/usr/bin/env python3
"""
Pi-local replay executor for quantized robot-arm JSON plans.

This node intentionally has no dependency on the laptop training project. Copy a
pi_replay_plan_v1 JSON file to the Raspberry Pi, run this node next to
wicom_roboarm_unified_node.py, and it will publish timed JointTrajectory
segments using the Pi's local clock.

Publish modes:
  trajectory  - One JointTrajectory per segment (legacy, jittery).
  stream      - Per-segment S-curve interpolation at --stream-hz (less jittery).
  scurve      - Continuous 50Hz S-curve through ALL waypoints as one motion
                (smoothest, recommended). Inspired by uav_scurve_test.py.
  keyframe-scurve
              - S-curve between exported keyframes_deg, recommended for real servos.
"""

import argparse
import json
import math
import os
import time
from datetime import datetime

import rclpy
from rclpy.node import Node

from builtin_interfaces.msg import Duration
from sensor_msgs.msg import JointState
from std_msgs.msg import String
from std_srvs.srv import Trigger
from trajectory_msgs.msg import JointTrajectory, JointTrajectoryPoint


def _duration_msg(duration_sec: float) -> Duration:
    sec = int(duration_sec)
    nanosec = int((float(duration_sec) - sec) * 1e9)
    return Duration(sec=sec, nanosec=nanosec)


def _load_plan(path: str) -> dict:
    with open(os.path.expanduser(path), "r", encoding="utf-8") as f:
        plan = json.load(f)

    if plan.get("schema") != "pi_replay_plan_v1":
        raise ValueError(f"Unsupported replay schema: {plan.get('schema')}")
    if not plan.get("joint_names"):
        raise ValueError("Replay plan missing joint_names")
    if not plan.get("segments"):
        raise ValueError("Replay plan has no segments")

    joint_names = list(plan["joint_names"])
    for idx, seg in enumerate(plan["segments"]):
        names = list(seg.get("joint_names") or joint_names)
        positions = list(seg.get("positions_deg", []))
        duration = float(seg.get("duration_sec", 0.0))
        if len(names) != len(positions):
            raise ValueError(
                f"Segment {idx} has {len(names)} joint names but {len(positions)} positions"
            )
        if duration <= 0.0:
            raise ValueError(f"Segment {idx} has invalid duration_sec={duration}")
        for name, pos in zip(names, positions):
            value = float(pos)
            if value < 0.0 or value > 180.0:
                raise ValueError(f"Segment {idx} joint {name} outside [0, 180] deg: {value}")

    for idx, keyframe in enumerate(plan.get("keyframes_deg", []) or []):
        names = list(keyframe.get("joint_names") or joint_names)
        positions = list(keyframe.get("positions_deg", []))
        if len(names) != len(positions):
            raise ValueError(
                f"Keyframe {idx} has {len(names)} joint names but {len(positions)} positions"
            )
        for name, pos in zip(names, positions):
            value = float(pos)
            if value < 0.0 or value > 180.0:
                raise ValueError(f"Keyframe {idx} joint {name} outside [0, 180] deg: {value}")
    return plan


def _min_jerk(t: float) -> float:
    """S-Curve 5th-order: S(t) = 10t³ - 15t⁴ + 6t⁵  for t ∈ [0,1]."""
    t = max(0.0, min(1.0, t))
    t3 = t * t * t
    return 10.0 * t3 - 15.0 * t3 * t + 6.0 * t3 * t * t


class PiReplayExecutor(Node):
    def __init__(self, args, plan):
        super().__init__("pi_replay_executor")
        self.args = args
        self.plan = plan
        self.joint_names = list(plan["joint_names"])
        self.tolerance_deg = float(args.tolerance_deg if args.tolerance_deg is not None else plan.get("joint_error_tolerance_deg", 2.0))

        self.trajectory_pub = self.create_publisher(JointTrajectory, args.trajectory_topic, 10)
        self.command_pub = self.create_publisher(JointState, args.command_topic, 10)
        self.status_pub = self.create_publisher(String, args.status_topic, 10)
        self.joint_state_sub = self.create_subscription(
            JointState,
            args.joint_state_topic,
            self._joint_state_cb,
            10,
        )
        self.hardware_status_sub = self.create_subscription(
            String,
            args.hardware_status_topic,
            self._hardware_status_cb,
            10,
        )
        self.home_client = self.create_client(Trigger, args.home_service)

        self.latest_joint_state_deg = None
        self.latest_joint_state_time = 0.0
        self.latest_i2c_error_time = 0.0
        self.latest_i2c_error_detail = ""

        log_dir = os.path.abspath(os.path.expanduser(args.log_dir))
        os.makedirs(log_dir, exist_ok=True)
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        self.log_path = os.path.join(log_dir, f"pi_replay_executor_log_{timestamp}.jsonl")

    def _joint_state_cb(self, msg: JointState):
        values = {}
        for name, value in zip(msg.name, msg.position):
            angle = float(value)
            # wicom_roboarm publishes radians; keep degree compatibility for
            # older nodes or hand-made test publishers.
            if abs(angle) < 6.3:
                angle = math.degrees(angle)
            values[name] = angle
        if values:
            self.latest_joint_state_deg = values
            self.latest_joint_state_time = time.monotonic()

    def _hardware_status_cb(self, msg: String):
        data = str(msg.data)
        if data.startswith("I2C_ERROR"):
            self.latest_i2c_error_time = time.monotonic()
            self.latest_i2c_error_detail = data

    def _publish_status(self, payload: dict):
        msg = String()
        msg.data = json.dumps(payload, separators=(",", ":"))
        self.status_pub.publish(msg)

    def _write_log(self, payload: dict):
        with open(self.log_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(payload, separators=(",", ":")) + "\n")

    def _status(self, payload: dict):
        self._publish_status(payload)
        self._write_log(payload)

    def _home(self):
        if self.args.no_home:
            return
        if not self.home_client.wait_for_service(timeout_sec=10.0):
            self.get_logger().warn(f"Home service not available: {self.args.home_service}")
            return
        future = self.home_client.call_async(Trigger.Request())
        rclpy.spin_until_future_complete(self, future, timeout_sec=5.0)
        result = future.result()
        if result is None:
            self.get_logger().warn("Home service timed out")
        elif result.success:
            self.get_logger().info("Home service completed")
        else:
            self.get_logger().warn(f"Home service failed: {result.message}")

    def _build_trajectory(self, names, positions_deg, duration_sec):
        traj = JointTrajectory()
        traj.header.stamp = self.get_clock().now().to_msg()
        traj.joint_names = list(names)
        point = JointTrajectoryPoint()
        point.positions = [float(v) for v in positions_deg]
        point.time_from_start = _duration_msg(duration_sec)
        traj.points = [point]
        return traj

    def _build_command(self, names, positions_deg):
        msg = JointState()
        msg.header.stamp = self.get_clock().now().to_msg()
        msg.name = list(names)
        msg.position = [float(v) for v in positions_deg]
        return msg

    def _shape_progress(self, alpha: float) -> float:
        alpha = max(0.0, min(float(alpha), 1.0))
        profile = str(self.args.stream_profile).strip().lower()
        if profile in {"min_jerk", "minimum_jerk", "s_curve", "scurve"}:
            return _min_jerk(alpha)
        return alpha

    def _current_or_previous_start(self, names, previous_cmd_deg):
        if previous_cmd_deg:
            return [float(previous_cmd_deg.get(name, 90.0)) for name in names]
        if self.latest_joint_state_deg:
            return [float(self.latest_joint_state_deg.get(name, 90.0)) for name in names]
        return [90.0 for _ in names]

    def _plan_keyframes(self):
        keyframes = []
        raw_keyframes = self.plan.get("keyframes_deg") or []
        if raw_keyframes:
            for idx, keyframe in enumerate(raw_keyframes):
                names = list(keyframe.get("joint_names") or self.joint_names)
                positions_deg = [float(v) for v in keyframe["positions_deg"]]
                keyframes.append({
                    "idx": int(keyframe.get("idx", idx)),
                    "source_idx": int(keyframe.get("source_waypoint_idx", idx)),
                    "joint_names": names,
                    "positions_deg": positions_deg,
                    "source": "keyframes_deg",
                })
            return keyframes

        self.get_logger().warn(
            "Plan has no keyframes_deg; keyframe-scurve will fall back to segments. "
            "Re-export JSON after this update for smoother real replay."
        )
        for idx, seg in enumerate(self.plan["segments"]):
            keyframes.append({
                "idx": int(seg.get("idx", idx)),
                "source_idx": int(seg.get("sample_index", idx)),
                "joint_names": list(seg.get("joint_names") or self.joint_names),
                "positions_deg": [float(v) for v in seg["positions_deg"]],
                "source": "segments_fallback",
            })
        return keyframes

    def _move_time_for_keyframe(self, current_deg, target_deg):
        fixed_time = float(self.args.move_time_sec)
        if fixed_time > 0.0:
            return fixed_time

        max_delta = max(
            abs(float(target) - float(current))
            for current, target in zip(current_deg, target_deg)
        )
        max_speed = max(1e-6, float(self.args.max_speed_deg_s))
        return max(float(self.args.min_move_time_sec), max_delta / max_speed)

    def _execute_segment_stream(self, names, target_deg, duration_sec, previous_cmd_deg):
        start_deg = self._current_or_previous_start(names, previous_cmd_deg)
        stream_hz = max(1.0, float(self.args.stream_hz))
        dt = 1.0 / stream_hz
        t0 = time.monotonic()
        next_tick = t0

        while rclpy.ok():
            now = time.monotonic()
            alpha = (now - t0) / max(duration_sec, 1e-6)
            if alpha >= 1.0:
                break
            shaped = self._shape_progress(alpha)
            cmd = [
                start + shaped * (target - start)
                for start, target in zip(start_deg, target_deg)
            ]
            self.command_pub.publish(self._build_command(names, cmd))
            rclpy.spin_once(self, timeout_sec=0.0)
            next_tick += dt
            sleep_sec = next_tick - time.monotonic()
            if sleep_sec > 0.0:
                time.sleep(sleep_sec)

        self.command_pub.publish(self._build_command(names, target_deg))

    # ─── S-Curve Continuous Mode ───────────────────────────────────────
    # Treats ALL segments as keyframes of one continuous trajectory.
    # Interpolates at 50Hz using min-jerk between each adjacent keyframe pair,
    # publishing /pca9685_servo/command directly. No trajectory resets.
    # This is the same approach as uav_scurve_test.py.

    def _execute_episode_scurve(self, episode: int, replay_rate_override, started: float):
        """Execute one full episode as a continuous S-curve stream through all waypoints."""
        segments = self.plan["segments"]
        stream_hz = max(1.0, float(self.args.stream_hz))
        dt = 1.0 / stream_hz

        # Build the keyframe list: each entry is (names, target_deg[], move_time_sec)
        keyframes = []
        for seg in segments:
            names = list(seg.get("joint_names") or self.joint_names)
            positions_deg = [float(v) for v in seg["positions_deg"]]
            plan_duration_sec = float(seg["duration_sec"])
            duration_sec = (1.0 / replay_rate_override) if replay_rate_override is not None else plan_duration_sec
            keyframes.append((names, positions_deg, duration_sec))

        if not keyframes:
            return 0, 0, 0

        # Determine starting position
        start_deg = self._current_or_previous_start(
            keyframes[0][0],
            None,
        )
        if self.latest_joint_state_deg:
            start_deg = [
                float(self.latest_joint_state_deg.get(name, 90.0))
                for name in keyframes[0][0]
            ]

        ok_count = 0
        lag_count = 0
        lost_count = 0

        # Move to first keyframe with S-curve (settling move)
        first_names, first_target, first_dur = keyframes[0]
        settle_time = max(first_dur, 0.5)  # at least 0.5s for the first move
        self.get_logger().info(
            f"S-curve Ep {episode + 1}: settling to first waypoint ({settle_time:.2f}s)..."
        )
        self._run_s_curve_move(first_names, start_deg, first_target, settle_time, dt)

        # Log the first segment
        status, errors, max_error, feedback_age = self._measure_error(first_names, first_target)
        actual_deg = self._get_actual_deg(first_names)
        if status == "OK":
            ok_count += 1
        elif status == "LAG":
            lag_count += 1
        else:
            lost_count += 1

        self._status({
            "event": "SEGMENT_DONE",
            "episode": episode + 1,
            "segment_idx": 0,
            "duration_sec": settle_time,
            "cmd_deg": {n: p for n, p in zip(first_names, first_target)},
            "actual_deg": actual_deg,
            "max_err_deg": round(max_error, 4),
            "feedback_age_sec": round(feedback_age, 4),
            "status": status,
            "elapsed_sec": round(time.monotonic() - started, 4),
        })

        if self.args.print_segments:
            self.get_logger().info(
                f"Ep {episode + 1} Seg 1/{len(keyframes)}: "
                f"{status} max_err={max_error:.2f}deg"
            )

        # Stream through remaining keyframes with S-curve interpolation
        current_pos = list(first_target)
        for kf_idx in range(1, len(keyframes)):
            names, target_deg, move_time = keyframes[kf_idx]

            self._run_s_curve_move(names, current_pos, target_deg, move_time, dt)

            # Measure error after this keyframe
            status, errors, max_error, feedback_age = self._measure_error(names, target_deg)
            actual_deg = self._get_actual_deg(names)

            if status == "OK":
                ok_count += 1
            elif status == "LAG":
                lag_count += 1
            else:
                lost_count += 1

            # Compute deltas for logging
            cmd_delta_deg = {}
            cmd_speed_deg_s = {}
            for name, target, prev in zip(names, target_deg, current_pos):
                delta = target - prev
                cmd_delta_deg[name] = round(delta, 4)
                cmd_speed_deg_s[name] = round(abs(delta) / max(move_time, 1e-6), 4)

            self._status({
                "event": "SEGMENT_DONE",
                "episode": episode + 1,
                "segment_idx": kf_idx,
                "duration_sec": move_time,
                "cmd_deg": {n: p for n, p in zip(names, target_deg)},
                "actual_deg": actual_deg,
                "cmd_delta_deg": cmd_delta_deg,
                "cmd_speed_deg_s": cmd_speed_deg_s,
                "max_err_deg": round(max_error, 4),
                "feedback_age_sec": round(feedback_age, 4),
                "status": status,
                "elapsed_sec": round(time.monotonic() - started, 4),
            })

            if self.args.print_segments:
                self.get_logger().info(
                    f"Ep {episode + 1} Seg {kf_idx + 1}/{len(keyframes)}: "
                    f"{status} max_err={max_error:.2f}deg"
                )

            current_pos = list(target_deg)

        return ok_count, lag_count, lost_count

    def _run_s_curve_move(self, names, start_deg, end_deg, move_time, dt):
        """
        Interpolate from start_deg to end_deg over move_time seconds using
        min-jerk S-curve, publishing JointState commands at 1/dt Hz.
        Exactly mirrors the approach in uav_scurve_test.py._run_s_curve().
        """
        steps = max(1, int(move_time / dt))
        t0 = time.monotonic()
        next_tick = t0
        publish_count = 0
        last_published = None
        deadband = max(0.0, float(self.args.deadband_deg))

        for step in range(steps + 1):
            t = step / steps
            s = _min_jerk(t)
            current = [
                s_val + s * (e_val - s_val)
                for s_val, e_val in zip(start_deg, end_deg)
            ]
            force = step == 0 or step == steps
            if last_published is None:
                should_publish = True
            else:
                max_delta = max(
                    abs(float(now) - float(prev))
                    for now, prev in zip(current, last_published)
                )
                should_publish = max_delta >= deadband

            if force or should_publish:
                self.command_pub.publish(self._build_command(names, current))
                rclpy.spin_once(self, timeout_sec=0.0)
                last_published = list(current)
                publish_count += 1

            next_tick += dt
            sleep_sec = next_tick - time.monotonic()
            if sleep_sec > 0.0:
                time.sleep(sleep_sec)

        # Ensure we land exactly on the target
        if last_published is None or any(
            abs(float(target) - float(prev)) > 1e-6
            for target, prev in zip(end_deg, last_published)
        ):
            self.command_pub.publish(self._build_command(names, end_deg))
            rclpy.spin_once(self, timeout_sec=0.0)
            publish_count += 1

        return publish_count

    def _execute_episode_keyframe_scurve(self, episode: int, started: float):
        keyframes = self._plan_keyframes()
        if not keyframes:
            return 0, 0, 0

        self.get_logger().info(
            f"Keyframe S-curve Ep {episode + 1}: keyframes={len(keyframes)} "
            f"stream_hz={self.args.stream_hz:.1f} deadband={self.args.deadband_deg:.2f}deg"
        )

        ok_count = 0
        lag_count = 0
        lost_count = 0
        stream_hz = max(1.0, float(self.args.stream_hz))
        dt = 1.0 / stream_hz
        current_pos = None

        for keyframe_idx, keyframe in enumerate(keyframes):
            names = keyframe["joint_names"]
            target_deg = keyframe["positions_deg"]

            if current_pos is None:
                current_pos = self._current_or_previous_start(names, None)

            move_time = self._move_time_for_keyframe(current_pos, target_deg)
            move_started = time.monotonic()

            if self.args.dry_run:
                publish_count = max(1, int(move_time * stream_hz))
                self.get_logger().info(
                    f"DRY Ep {episode + 1} Keyframe {keyframe_idx + 1}/{len(keyframes)}: "
                    f"{dict(zip(names, target_deg))} move_time={move_time:.3f}s "
                    f"est_publish={publish_count}"
                )
            else:
                publish_count = self._run_s_curve_move(
                    names,
                    current_pos,
                    target_deg,
                    move_time,
                    dt,
                )

            deadline = time.monotonic() + max(0.0, self.args.feedback_settle)
            while time.monotonic() < deadline:
                rclpy.spin_once(self, timeout_sec=0.01)

            status, errors, max_error, feedback_age = self._measure_error(
                names,
                target_deg,
                since_time=move_started,
            )
            actual_deg = self._get_actual_deg(names)
            cmd_delta_deg = {
                name: round(float(target) - float(current), 4)
                for name, target, current in zip(names, target_deg, current_pos)
            }
            cmd_speed_deg_s = {
                name: round(abs(delta) / max(move_time, 1e-6), 4)
                for name, delta in cmd_delta_deg.items()
            }

            if status == "OK":
                ok_count += 1
            elif status == "LAG":
                lag_count += 1
            elif status == "I2C_ERROR":
                lost_count += 1
            else:
                lost_count += 1

            self._status({
                "event": "KEYFRAME_DONE",
                "episode": episode + 1,
                "keyframe_idx": keyframe_idx,
                "source_idx": keyframe["source_idx"],
                "source": keyframe["source"],
                "duration_sec": move_time,
                "publish_count": publish_count,
                "cmd_deg": {name: pos for name, pos in zip(names, target_deg)},
                "actual_deg": actual_deg,
                "cmd_delta_deg": cmd_delta_deg,
                "cmd_speed_deg_s": cmd_speed_deg_s,
                "err_deg": errors,
                "hardware_error": self.latest_i2c_error_detail if status == "I2C_ERROR" else "",
                "max_err_deg": round(max_error, 4),
                "feedback_age_sec": round(feedback_age, 4),
                "status": status,
                "elapsed_sec": round(time.monotonic() - started, 4),
            })

            if self.args.print_segments:
                self.get_logger().info(
                    f"Ep {episode + 1}/{self.args.episodes} "
                    f"Keyframe {keyframe_idx + 1}/{len(keyframes)}: "
                    f"{status} max_err={max_error:.2f}deg "
                    f"move={move_time:.2f}s pubs={publish_count}"
                )

            current_pos = list(target_deg)

        return ok_count, lag_count, lost_count

    def _get_actual_deg(self, names):
        if self.latest_joint_state_deg is None:
            return {}
        return {
            name: round(float(self.latest_joint_state_deg[name]), 4)
            for name in names
            if name in self.latest_joint_state_deg
        }

    def _measure_error(self, names, positions_deg, since_time: float = 0.0):
        if since_time and self.latest_i2c_error_time >= since_time:
            return "I2C_ERROR", {}, 0.0, 0.0

        if self.latest_joint_state_deg is None:
            return "LOST", {}, 0.0, 999.0

        feedback_age = time.monotonic() - self.latest_joint_state_time
        errors = {}
        for name, cmd in zip(names, positions_deg):
            if name in self.latest_joint_state_deg:
                errors[name] = abs(float(cmd) - float(self.latest_joint_state_deg[name]))

        if len(errors) != len(names) or feedback_age > self.args.feedback_timeout:
            return "LOST", errors, max(errors.values()) if errors else 0.0, feedback_age

        max_error = max(errors.values()) if errors else 0.0
        return ("OK" if max_error <= self.tolerance_deg else "LAG"), errors, max_error, feedback_age

    def run(self):
        replay_rate_override = None
        if self.args.replay_rate_hz is not None:
            replay_rate_override = max(0.001, float(self.args.replay_rate_hz))
        effective_replay_rate = (
            replay_rate_override
            if replay_rate_override is not None
            else self.plan.get("replay_rate_hz")
        )
        self.get_logger().info(
            f"Pi-local replay: plan={self.args.plan} segments={len(self.plan['segments'])} "
            f"keyframes={len(self.plan.get('keyframes_deg') or [])} "
            f"episodes={self.args.episodes} replay_rate={effective_replay_rate}Hz "
            f"tolerance={self.tolerance_deg:.1f}deg publish_mode={self.args.publish_mode}"
        )
        self.get_logger().info(f"Writing log: {self.log_path}")

        start_payload = {
            "event": "START",
            "source_artifact": self.plan.get("source_artifact"),
            "segment_count": len(self.plan["segments"]),
            "keyframe_count": len(self.plan.get("keyframes_deg") or []),
            "plan_replay_rate_hz": self.plan.get("replay_rate_hz"),
            "effective_replay_rate_hz": effective_replay_rate,
            "duration_source": "override_replay_rate_hz" if replay_rate_override is not None else "plan_duration_sec",
            "trajectory_source": self.plan.get("trajectory_source"),
            "publish_mode": self.args.publish_mode,
            "stream_hz": self.args.stream_hz,
            "stream_profile": self.args.stream_profile,
            "move_time_sec": self.args.move_time_sec,
            "deadband_deg": self.args.deadband_deg,
            "tolerance_deg": self.tolerance_deg,
            "log_path": self.log_path,
        }
        self._status(start_payload)

        self._home()
        time.sleep(max(0.0, float(self.args.start_settle)))

        ok_count = 0
        lag_count = 0
        lost_count = 0
        started = time.monotonic()

        for episode in range(self.args.episodes):
            if episode > 0 and not self.args.no_home:
                self._home()
                time.sleep(max(0.0, float(self.args.start_settle)))

            if self.args.publish_mode == "keyframe-scurve":
                ep_ok, ep_lag, ep_lost = self._execute_episode_keyframe_scurve(
                    episode, started
                )
                ok_count += ep_ok
                lag_count += ep_lag
                lost_count += ep_lost
                continue

            # ── S-Curve continuous mode: execute entire episode as one smooth motion ──
            if self.args.publish_mode == "scurve" and not self.args.dry_run:
                ep_ok, ep_lag, ep_lost = self._execute_episode_scurve(
                    episode, replay_rate_override, started
                )
                ok_count += ep_ok
                lag_count += ep_lag
                lost_count += ep_lost
                continue

            # ── Legacy per-segment modes: trajectory / stream ──
            previous_cmd_deg = None
            for seg in self.plan["segments"]:
                names = list(seg.get("joint_names") or self.joint_names)
                positions_deg = [float(v) for v in seg["positions_deg"]]
                plan_duration_sec = float(seg["duration_sec"])
                duration_sec = (1.0 / replay_rate_override) if replay_rate_override is not None else plan_duration_sec
                seg_idx = int(seg.get("idx", 0))

                if self.args.dry_run:
                    self.get_logger().info(
                        f"DRY Ep {episode + 1} Seg {seg_idx}: "
                        f"{dict(zip(names, positions_deg))} dur={duration_sec:.3f}s"
                    )
                elif self.args.publish_mode == "stream":
                    self._execute_segment_stream(names, positions_deg, duration_sec, previous_cmd_deg)
                else:
                    self.trajectory_pub.publish(
                        self._build_trajectory(names, positions_deg, duration_sec)
                    )

                deadline = time.monotonic() + duration_sec + max(0.0, self.args.feedback_settle)
                while time.monotonic() < deadline:
                    rclpy.spin_once(self, timeout_sec=0.01)

                status, errors, max_error, feedback_age = self._measure_error(names, positions_deg)
                actual_deg = {}
                if self.latest_joint_state_deg:
                    actual_deg = {
                        name: round(float(self.latest_joint_state_deg[name]), 4)
                        for name in names
                        if name in self.latest_joint_state_deg
                    }
                cmd_delta_deg = {}
                cmd_speed_deg_s = {}
                if previous_cmd_deg:
                    for name, pos in zip(names, positions_deg):
                        if name not in previous_cmd_deg:
                            continue
                        delta = float(pos) - float(previous_cmd_deg[name])
                        cmd_delta_deg[name] = round(delta, 4)
                        cmd_speed_deg_s[name] = round(abs(delta) / max(duration_sec, 1e-6), 4)
                previous_cmd_deg = {name: pos for name, pos in zip(names, positions_deg)}

                if status == "OK":
                    ok_count += 1
                elif status == "LAG":
                    lag_count += 1
                else:
                    lost_count += 1

                payload = {
                    "event": "SEGMENT_DONE",
                    "episode": episode + 1,
                    "segment_idx": seg_idx,
                    "duration_sec": duration_sec,
                    "plan_duration_sec": plan_duration_sec,
                    "effective_replay_rate_hz": effective_replay_rate,
                    "cmd_deg": {name: pos for name, pos in zip(names, positions_deg)},
                    "actual_deg": actual_deg,
                    "cmd_delta_deg": cmd_delta_deg,
                    "cmd_speed_deg_s": cmd_speed_deg_s,
                    "err_deg": errors,
                    "max_err_deg": round(max_error, 4),
                    "feedback_age_sec": round(feedback_age, 4),
                    "status": status,
                    "elapsed_sec": round(time.monotonic() - started, 4),
                }
                self._status(payload)

                if self.args.print_segments:
                    self.get_logger().info(
                        f"Ep {episode + 1}/{self.args.episodes} "
                        f"Seg {seg_idx + 1}/{len(self.plan['segments'])}: "
                        f"{status} max_err={max_error:.2f}deg feedback_age={feedback_age:.3f}s"
                    )

        summary = {
            "event": "DONE",
            "ok_segments": ok_count,
            "lag_segments": lag_count,
            "lost_segments": lost_count,
            "total_segments": ok_count + lag_count + lost_count,
            "elapsed_sec": round(time.monotonic() - started, 4),
            "log_path": self.log_path,
        }
        self._status(summary)
        self.get_logger().info(
            f"Done: OK={ok_count} LAG={lag_count} LOST={lost_count} log={self.log_path}"
        )


def _parse_args():
    parser = argparse.ArgumentParser(description="Execute pi_replay_plan_v1 JSON on the Raspberry Pi.")
    parser.add_argument("--plan", required=True, help="Path to pi_replay_plan_v1 JSON on the Pi.")
    parser.add_argument("--episodes", type=int, default=1)
    parser.add_argument("--replay-rate-hz", type=float, default=None, help="Override replay speed on the Pi. If omitted, use duration_sec stored in JSON.")
    parser.add_argument("--tolerance-deg", type=float, default=None)
    parser.add_argument("--trajectory-topic", default="/pca9685_servo/trajectory")
    parser.add_argument("--command-topic", default="/pca9685_servo/command")
    parser.add_argument("--joint-state-topic", default="/pca9685_servo/joint_states")
    parser.add_argument("--hardware-status-topic", default="/pca9685_servo/hardware_status")
    parser.add_argument("--status-topic", default="/pca9685_servo/replay_status")
    parser.add_argument("--home-service", default="/pca9685_servo/home")
    parser.add_argument("--log-dir", default="~/ros2_ws/replay_logs")
    parser.add_argument("--feedback-timeout", type=float, default=0.5)
    parser.add_argument("--feedback-settle", type=float, default=0.03)
    parser.add_argument("--start-settle", type=float, default=1.0)
    parser.add_argument("--publish-mode", choices=["keyframe-scurve", "stream", "trajectory", "scurve"], default="keyframe-scurve",
                        help="keyframe-scurve: S-curve between exported keyframes_deg, recommended. "
                             "scurve: continuous S-curve through all segment waypoints. "
                             "stream: per-segment interpolation. trajectory: one timed msg per segment.")
    parser.add_argument("--stream-hz", type=float, default=10.0, help="Command publish rate for keyframe-scurve/stream/scurve modes.")
    parser.add_argument("--stream-profile", choices=["linear", "min_jerk"], default="min_jerk",
                        help="Interpolation profile for stream mode (scurve always uses min_jerk).")
    parser.add_argument("--move-time-sec", type=float, default=1.2,
                        help="Fixed seconds for each keyframe-scurve move. Use 0 to derive from --max-speed-deg-s.")
    parser.add_argument("--min-move-time-sec", type=float, default=0.8,
                        help="Minimum keyframe-scurve move duration when --move-time-sec is 0.")
    parser.add_argument("--max-speed-deg-s", type=float, default=35.0,
                        help="Max joint speed used when --move-time-sec is 0.")
    parser.add_argument("--deadband-deg", type=float, default=0.5,
                        help="Suppress command publishes whose max joint change is below this threshold.")
    parser.add_argument("--no-home", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--print-segments", action="store_true")
    args, ros_args = parser.parse_known_args()
    return args, ros_args


def main():
    args, ros_args = _parse_args()
    plan = _load_plan(args.plan)
    rclpy.init(args=ros_args)
    node = PiReplayExecutor(args, plan)
    try:
        node.run()
    except KeyboardInterrupt:
        node.get_logger().warn("Replay interrupted by user")
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
