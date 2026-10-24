#!/usr/bin/env python3
"""
Unified ROS2 servo controller for 6-DOF robot arm.

Hardware:
  - PCA9685 connected directly to Raspberry Pi 4 I2C bus 1 (no mux)
  - CH0: Base        (TD-8120MG)
  - CH1: Shoulder    (TD-8120MG)
  - CH2: Elbow       (MG996R)
  - CH3: Wrist Roll  (MG90S)
  - CH4: Wrist Pitch (MG90S)
  - CH5: Pen/Gripper (MG90S)

Supports per-joint pulse-width calibration via:
  pulse_us_min_by_joint / pulse_us_max_by_joint
"""
import math
import time
import threading

import rclpy
from rclpy.node import Node
from rcl_interfaces.msg import ParameterDescriptor, ParameterType

from sensor_msgs.msg import JointState
from std_srvs.srv import Trigger
from trajectory_msgs.msg import JointTrajectory

from smbus2 import SMBus


# ─── helpers ───

def _parse_i2c_addr(val, default):
    if val is None:
        return default
    if isinstance(val, int):
        return val
    if isinstance(val, str):
        try:
            return int(val.strip(), 0)
        except ValueError:
            return default
    return default


def _parse_int(val, default):
    if val is None:
        return default
    if isinstance(val, int):
        return val
    if isinstance(val, str):
        try:
            return int(val.strip(), 0)
        except ValueError:
            return default
    try:
        return int(val)
    except Exception:
        return default


# ─── PCA9685 registers ───

MODE1      = 0x00
MODE2      = 0x01
PRESCALE   = 0xFE
LED0_ON_L  = 0x06
LED0_ON_H  = 0x07
LED0_OFF_L = 0x08
LED0_OFF_H = 0x09
ALL_LED_ON_L  = 0xFA
ALL_LED_ON_H  = 0xFB
ALL_LED_OFF_L = 0xFC
ALL_LED_OFF_H = 0xFD

RESTART = 0x80
SLEEP   = 0x10
ALLCALL = 0x01
OUTDRV  = 0x04


class PCA9685:
    """Direct PCA9685 driver over smbus2 (no mux)."""

    def __init__(self, bus: SMBus, address: int, oscillator_hz: int = 25_000_000):
        self._bus = bus
        self._addr = address
        self._osc = oscillator_hz
        self._init_device()

    def _write8(self, reg, val):
        self._bus.write_byte_data(self._addr, reg, val & 0xFF)

    def _read8(self, reg):
        return self._bus.read_byte_data(self._addr, reg)

    def _init_device(self):
        self._write8(MODE2, OUTDRV)
        self._write8(MODE1, ALLCALL)
        time.sleep(0.005)
        mode1 = self._read8(MODE1) & ~SLEEP
        self._write8(MODE1, mode1)
        time.sleep(0.005)

    def set_pwm_freq(self, freq_hz: float):
        prescaleval = float(self._osc) / (4096.0 * float(freq_hz)) - 1.0
        prescale = int(math.floor(prescaleval + 0.5))
        oldmode = self._read8(MODE1)
        newmode = (oldmode & 0x7F) | SLEEP
        self._write8(MODE1, newmode)
        self._write8(PRESCALE, prescale)
        self._write8(MODE1, oldmode)
        time.sleep(0.005)
        self._write8(MODE1, oldmode | RESTART)

    def set_pwm(self, channel: int, on: int, off: int):
        base = LED0_ON_L + 4 * channel
        for attempt in range(3):
            try:
                self._write8(base + 0, on & 0xFF)
                self._write8(base + 1, (on >> 8) & 0x0F)
                self._write8(base + 2, off & 0xFF)
                self._write8(base + 3, (off >> 8) & 0x0F)
                return
            except OSError:
                if attempt == 2:
                    raise
                time.sleep(0.001)

    def set_off(self, channel: int):
        base = LED0_ON_L + 4 * channel
        self._write8(base + 0, 0x00)
        self._write8(base + 1, 0x00)
        self._write8(base + 2, 0x00)
        self._write8(base + 3, 0x10)

    def set_all_off(self):
        self._write8(ALL_LED_ON_L, 0)
        self._write8(ALL_LED_ON_H, 0)
        self._write8(ALL_LED_OFF_L, 0)
        self._write8(ALL_LED_OFF_H, 0x10)


class UnifiedRoboArmNode(Node):
    def __init__(self):
        super().__init__("wicom_roboarm_unified")

        # ---- typed descriptors (avoid [] => BYTE_ARRAY) ----
        desc_str_arr = ParameterDescriptor(type=ParameterType.PARAMETER_STRING_ARRAY)
        desc_int_arr = ParameterDescriptor(type=ParameterType.PARAMETER_INTEGER_ARRAY)
        desc_dbl_arr = ParameterDescriptor(type=ParameterType.PARAMETER_DOUBLE_ARRAY)

        # -------- Parameters ----------
        self.declare_parameter("i2c_bus", 1)
        self.declare_parameter("i2c_address", "0x40")
        self.declare_parameter("oscillator_hz", 25_000_000)
        self.declare_parameter("pwm_frequency_hz", 50.0)
        self.declare_parameter("enable_on_start", False)
        self.declare_parameter("use_mux", False)   # kept for compat, but always False now

        # Global pulse defaults
        self.declare_parameter("pulse_us_min", 500.0)
        self.declare_parameter("pulse_us_max", 2500.0)
        self.declare_parameter("period_us", 20000.0)

        self.declare_parameter("neutral_deg", 90.0)

        # Joint arrays (typed)
        self.declare_parameter("joint_names", ["__dummy__"], desc_str_arr)
        self.declare_parameter("channels", [0], desc_int_arr)

        # Per-joint pulse calibration arrays
        self.declare_parameter("pulse_us_min_by_joint", [0.0], desc_dbl_arr)
        self.declare_parameter("pulse_us_max_by_joint", [0.0], desc_dbl_arr)

        # Per-joint neutral and limits
        self.declare_parameter("neutral_deg_by_joint", [0.0], desc_dbl_arr)
        self.declare_parameter("limits_min_deg_by_joint", [0.0], desc_dbl_arr)
        self.declare_parameter("limits_max_deg_by_joint", [180.0], desc_dbl_arr)

        self.declare_parameter("publish_rate_hz", 10.0)
        self.declare_parameter("trajectory_update_rate_hz", 25.0)
        self.declare_parameter("trajectory_profile", "min_jerk")  # linear|min_jerk
        self.declare_parameter("default_moving_time_sec", 0.0)
        self.declare_parameter("command_deadband_deg", 0.0)
        self.declare_parameter("command_timeout_sec", 1.0)
        self.declare_parameter("timeout_behavior", "hold")     # hold|neutral|off
        self.declare_parameter("shutdown_behavior", "neutral")  # hold|neutral|off

        # -------- Read Parameters ----------
        self.busnum          = _parse_int(self.get_parameter("i2c_bus").value, 1)
        self.pca_address     = _parse_i2c_addr(self.get_parameter("i2c_address").value, 0x40)
        self.oscillator_hz   = _parse_int(self.get_parameter("oscillator_hz").value, 25_000_000)
        self.pwm_freq        = float(self.get_parameter("pwm_frequency_hz").value)
        self.enable_on_start = bool(self.get_parameter("enable_on_start").value)

        # Global pulse defaults
        self.pulse_us_min_global = float(self.get_parameter("pulse_us_min").value)
        self.pulse_us_max_global = float(self.get_parameter("pulse_us_max").value)
        self.period_us           = float(self.get_parameter("period_us").value)

        self.neutral_deg     = float(self.get_parameter("neutral_deg").value)

        self.publish_rate_hz     = float(self.get_parameter("publish_rate_hz").value)
        self.trajectory_update_rate_hz = float(self.get_parameter("trajectory_update_rate_hz").value)
        self.trajectory_profile = str(self.get_parameter("trajectory_profile").value).strip().lower()
        self.default_moving_time_sec = float(self.get_parameter("default_moving_time_sec").value)
        self.command_deadband_deg = max(0.0, float(self.get_parameter("command_deadband_deg").value))
        self.command_timeout_sec = float(self.get_parameter("command_timeout_sec").value)
        self.timeout_behavior    = str(self.get_parameter("timeout_behavior").value)
        self.shutdown_behavior   = str(self.get_parameter("shutdown_behavior").value)

        # ---- Joint config ----
        joint_names = list(self.get_parameter("joint_names").value)
        channels = list(self.get_parameter("channels").value)

        # strip dummy defaults
        if joint_names == ["__dummy__"]:
            joint_names = []
        if channels == [0] and not joint_names:
            channels = []

        if (not joint_names) or (not channels) or len(joint_names) != len(channels):
            raise RuntimeError("joint_names and channels must be set and same length.")

        self.joint_names = joint_names
        self.channels = channels
        self.num_joints = len(self.joint_names)
        self.name_to_idx = {n: i for i, n in enumerate(self.joint_names)}
        self.channel_by_idx = {i: ch for i, ch in enumerate(self.channels)}

        # ---- Per-joint pulse calibration ----
        pmin_list = list(self.get_parameter("pulse_us_min_by_joint").value)
        pmax_list = list(self.get_parameter("pulse_us_max_by_joint").value)

        # If dummy/missing, fall back to global
        if pmin_list == [0.0] and self.num_joints != 1:
            pmin_list = []
        if pmax_list == [0.0] and self.num_joints != 1:
            pmax_list = []

        if len(pmin_list) == self.num_joints:
            self.pulse_us_min_by_idx = [float(x) for x in pmin_list]
        else:
            self.pulse_us_min_by_idx = [self.pulse_us_min_global] * self.num_joints

        if len(pmax_list) == self.num_joints:
            self.pulse_us_max_by_idx = [float(x) for x in pmax_list]
        else:
            self.pulse_us_max_by_idx = [self.pulse_us_max_global] * self.num_joints

        # ---- Per-joint neutral and limits ----
        neutral_list = list(self.get_parameter("neutral_deg_by_joint").value)
        if neutral_list == [0.0] and self.num_joints != 1:
            neutral_list = []
        if len(neutral_list) == self.num_joints:
            self.neutral_deg_by_idx = [float(x) for x in neutral_list]
        else:
            self.neutral_deg_by_idx = [self.neutral_deg] * self.num_joints

        min_list = list(self.get_parameter("limits_min_deg_by_joint").value)
        max_list = list(self.get_parameter("limits_max_deg_by_joint").value)
        if min_list == [0.0] and self.num_joints != 1:
            min_list = []
        if max_list == [180.0] and self.num_joints != 1:
            max_list = []
        if len(min_list) == self.num_joints and len(max_list) == self.num_joints:
            self.limits_min_by_idx = [float(x) for x in min_list]
            self.limits_max_by_idx = [float(x) for x in max_list]
        else:
            self.limits_min_by_idx = [0.0] * self.num_joints
            self.limits_max_by_idx = [180.0] * self.num_joints

        self.current_deg = list(self.neutral_deg_by_idx)
        self.last_cmd_time = [self._now_s()] * self.num_joints
        self.enabled = self.enable_on_start
        self.active_trajectory = None

        # -------- I2C & PCA9685 (direct, no mux) ----------
        self.lock = threading.Lock()
        self.bus_smbus = SMBus(self.busnum)

        self.pca = PCA9685(
            self.bus_smbus,
            self.pca_address,
            oscillator_hz=self.oscillator_hz,
        )
        self.pca.set_pwm_freq(self.pwm_freq)

        if self.enabled:
            self._apply_all(self.current_deg)
        else:
            self._apply_behavior_all("off")

        # -------- Pub/Sub / Services --------
        self.pub_joint = self.create_publisher(JointState, "joint_states", 10)

        self.sub_command = self.create_subscription(JointState, "command", self._on_command, 10)
        self.sub_trajectory = self.create_subscription(JointTrajectory, "trajectory", self._on_trajectory, 10)

        self.srv_enable  = self.create_service(Trigger, "enable", self.handle_enable)
        self.srv_disable = self.create_service(Trigger, "disable", self.handle_disable)
        self.srv_home    = self.create_service(Trigger, "home", self.handle_home)

        self.pub_timer = self.create_timer(1.0 / max(self.publish_rate_hz, 1.0), self._publish_joint_state)
        self.trajectory_timer = self.create_timer(
            1.0 / max(self.trajectory_update_rate_hz, 1.0),
            self._trajectory_tick
        )
        self.watchdog_timer = self.create_timer(0.05, self._watchdog_tick)

        # Log servo config summary
        servo_info = []
        for i, name in enumerate(self.joint_names):
            servo_info.append(
                f"{name}(CH{self.channels[i]}): "
                f"{self.pulse_us_min_by_idx[i]:.0f}-{self.pulse_us_max_by_idx[i]:.0f}µs"
            )
        self.get_logger().info(
            f"Unified RoboArm started (no mux, direct PCA9685): "
            f"PCA=0x{self.pca_address:02X} pwm={self.pwm_freq:.0f}Hz "
            f"enabled={self.enabled} trajectory_profile={self.trajectory_profile} "
            f"trajectory_update={self.trajectory_update_rate_hz:.0f}Hz "
            f"default_move={self.default_moving_time_sec:.2f}s "
            f"deadband={self.command_deadband_deg:.2f}deg | "
            + " | ".join(servo_info)
        )

    def _now_s(self) -> float:
        return self.get_clock().now().nanoseconds / 1e9

    # ─── Pulse / angle conversion (per-joint) ───

    def _pulse_us_for_angle(self, angle_deg: float, joint_idx: int) -> float:
        """Convert 0-180° to pulse width (µs), using per-joint calibration."""
        angle = max(0.0, min(180.0, float(angle_deg)))
        pmin = self.pulse_us_min_by_idx[joint_idx]
        pmax = self.pulse_us_max_by_idx[joint_idx]
        return pmin + (angle / 180.0) * (pmax - pmin)

    def _pulse_us_to_counts(self, pulse_us: float) -> int:
        """Convert pulse width (µs) to PCA9685 12-bit count."""
        if self.period_us <= 0:
            self.period_us = 20000.0
        counts = int(round((float(pulse_us) / float(self.period_us)) * 4096.0))
        return max(0, min(4095, counts))

    def angle_to_count(self, angle_deg: float, joint_idx: int) -> int:
        return self._pulse_us_to_counts(self._pulse_us_for_angle(angle_deg, joint_idx))

    # ─── Servo application ───

    def apply_joint(self, idx: int, angle_deg: float):
        ch = self.channel_by_idx[idx]
        counts = self.angle_to_count(angle_deg, idx)
        with self.lock:
            self.pca.set_pwm(ch, 0, counts)

    def _move_to_neutral(self, idx: int):
        neutral = self.neutral_deg_by_idx[idx]
        self.apply_joint(idx, neutral)
        self.current_deg[idx] = neutral

    def _turn_off(self, idx: int):
        ch = self.channel_by_idx[idx]
        with self.lock:
            self.pca.set_off(ch)

    def _apply_behavior_all(self, behavior: str):
        for idx in range(self.num_joints):
            if behavior == "hold":
                self.apply_joint(idx, self.current_deg[idx])
            elif behavior == "neutral":
                self._move_to_neutral(idx)
            elif behavior == "off":
                self._turn_off(idx)

    def _apply_all(self, deg_list):
        for idx, deg in enumerate(deg_list):
            self.apply_joint(idx, deg)

    def _cancel_active_trajectory(self):
        self.active_trajectory = None

    def _set_joint_target_now(self, idx: int, target: float):
        self.apply_joint(idx, target)
        self.current_deg[idx] = target
        self.last_cmd_time[idx] = self._now_s()

    def _shape_trajectory_progress(self, alpha: float) -> float:
        alpha = max(0.0, min(float(alpha), 1.0))
        if self.trajectory_profile in ("min_jerk", "minimum_jerk", "s_curve", "scurve"):
            a2 = alpha * alpha
            a3 = a2 * alpha
            return 10.0 * a3 - 15.0 * a3 * alpha + 6.0 * a3 * a2
        return alpha

    def _start_timed_trajectory(self, target_deg_by_idx, duration_sec: float, source: str):
        now_wall = time.monotonic()
        addressed_indices = sorted(target_deg_by_idx.keys())
        if not addressed_indices:
            return

        if duration_sec <= 0.0:
            self._cancel_active_trajectory()
            for idx in addressed_indices:
                self._set_joint_target_now(idx, target_deg_by_idx[idx])
            return

        self.active_trajectory = {
            "start_wall": now_wall,
            "duration_sec": float(duration_sec),
            "start_deg": list(self.current_deg),
            "target_deg": dict(target_deg_by_idx),
            "indices": addressed_indices,
            "source": source,
        }
        now_ros = self._now_s()
        for idx in addressed_indices:
            self.last_cmd_time[idx] = now_ros

        names = ', '.join(self.joint_names[idx] for idx in addressed_indices)
        self.get_logger().info(
            f"Timed trajectory accepted from {source}: joints=[{names}] "
            f"dur={duration_sec:.2f}s profile={self.trajectory_profile}"
        )

    # ─── ROS callbacks ───

    def _on_command(self, msg: JointState):
        if not msg.name or not msg.position:
            return

        if not self.enabled:
            self.get_logger().warn("Auto enable outputs, received /command JointState")
            self.enabled = True

        target_deg_by_idx = {}

        for name, pos in zip(msg.name, msg.position):
            if name not in self.name_to_idx:
                continue
            idx = self.name_to_idx[name]

            angle = float(pos)
            # Auto-detect radians (small values) vs degrees
            if abs(angle) < 6.3:
                angle = math.degrees(angle)

            target = max(self.limits_min_by_idx[idx], min(self.limits_max_by_idx[idx], angle))
            if abs(target - float(self.current_deg[idx])) < self.command_deadband_deg:
                continue
            target_deg_by_idx[idx] = target

        if not target_deg_by_idx:
            return

        try:
            if self.default_moving_time_sec > 0.0:
                self._start_timed_trajectory(
                    target_deg_by_idx,
                    self.default_moving_time_sec,
                    source="command",
                )
            else:
                self._cancel_active_trajectory()
                for idx, target in target_deg_by_idx.items():
                    self._set_joint_target_now(idx, target)
        except Exception as e:
            self.get_logger().error(f"I2C error command: {e}")

    def _on_trajectory(self, msg: JointTrajectory):
        if not msg.joint_names or not msg.points:
            return

        if not self.enabled:
            self.get_logger().warn("Auto enable outputs, received /trajectory JointTrajectory")
            self.enabled = True

        point = msg.points[0]
        if not point.positions:
            return

        duration_sec = float(point.time_from_start.sec) + float(point.time_from_start.nanosec) / 1e9
        target_deg_by_idx = {}

        for name, pos in zip(msg.joint_names, point.positions):
            if name not in self.name_to_idx:
                continue
            idx = self.name_to_idx[name]
            angle = float(pos)
            if abs(angle) < 6.3:
                angle = math.degrees(angle)
            target = max(self.limits_min_by_idx[idx], min(self.limits_max_by_idx[idx], angle))
            target_deg_by_idx[idx] = target

        self._start_timed_trajectory(target_deg_by_idx, duration_sec, source="trajectory")

    def _trajectory_tick(self):
        traj = self.active_trajectory
        if traj is None:
            return

        duration_sec = max(float(traj["duration_sec"]), 1e-6)
        elapsed = time.monotonic() - float(traj["start_wall"])
        alpha = max(0.0, min(elapsed / duration_sec, 1.0))
        shaped_alpha = self._shape_trajectory_progress(alpha)
        now_ros = self._now_s()

        for idx in traj["indices"]:
            start = float(traj["start_deg"][idx])
            target = float(traj["target_deg"][idx])
            commanded = start + shaped_alpha * (target - start)
            try:
                self.apply_joint(idx, commanded)
                self.current_deg[idx] = commanded
                self.last_cmd_time[idx] = now_ros
            except Exception as e:
                self.get_logger().error(f"I2C error during timed trajectory for {self.joint_names[idx]}: {e}")
                self._cancel_active_trajectory()
                return

        if alpha >= 1.0:
            self._cancel_active_trajectory()

    def handle_enable(self, _req, resp):
        try:
            self._apply_all(self.current_deg)
            self.enabled = True
            resp.success = True
            resp.message = "Outputs enabled"
            return resp
        except Exception as e:
            resp.success = False
            resp.message = str(e)
            return resp

    def handle_disable(self, _req, resp):
        try:
            self._cancel_active_trajectory()
            self._apply_behavior_all("off")
            self.enabled = False
            resp.success = True
            resp.message = "Outputs disabled (off)"
            return resp
        except Exception as e:
            resp.success = False
            resp.message = str(e)
            return resp

    def handle_home(self, _req, resp):
        try:
            self._cancel_active_trajectory()
            for idx in range(self.num_joints):
                self._move_to_neutral(idx)
            self.enabled = True
            resp.success = True
            resp.message = "All joints neutral"
            return resp
        except Exception as e:
            resp.success = False
            resp.message = str(e)
            return resp

    def _publish_joint_state(self):
        js = JointState()
        js.header.stamp = self.get_clock().now().to_msg()
        js.name = list(self.joint_names)
        js.position = [math.radians(d) for d in self.current_deg]
        self.pub_joint.publish(js)

    def _watchdog_tick(self):
        if self.command_timeout_sec <= 0:
            return
        now = self._now_s()
        for idx in range(self.num_joints):
            if (now - self.last_cmd_time[idx]) > self.command_timeout_sec:
                if self.timeout_behavior == "hold":
                    pass
                elif self.timeout_behavior == "neutral":
                    self._move_to_neutral(idx)
                elif self.timeout_behavior == "off":
                    self._turn_off(idx)
                self.last_cmd_time[idx] = now

    def destroy_node(self):
        self.get_logger().warn(f"Shutdown: apply behavior {self.shutdown_behavior} for all servos")
        try:
            self._apply_behavior_all(self.shutdown_behavior)
            time.sleep(0.01)
            with self.lock:
                self.pca.set_all_off()
            self.bus_smbus.close()
        except Exception as e:
            self.get_logger().error(f"Shutdown error: {e}")
        return super().destroy_node()


def main():
    rclpy.init()
    node = UnifiedRoboArmNode()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()
