#!/usr/bin/env python3
"""
Robotic Arm Control Node for Virtual-Copilot-UAM (ROS2 Humble)

Subscribes to servo angle commands from the UDP server node and drives the
5-DOF robotic arm through a PCA9685 board.

Every joint is calibrated independently (angle limits, pulse width, direction,
offset) through config/roboarm_params.yaml, so the node never has to guess the
mechanical limits of the arm it is driving.

Migrated from ROS1 (wicom_roboarm) to ROS2 Humble.
"""

import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rcl_interfaces.msg import SetParametersResult
from std_msgs.msg import Float32MultiArray

# Servo order sent by Unity (Uavlink_msg_servo_control_t)
JOINT_NAMES = ['base', 'shoulder', 'elbow', 'wrist', 'gripper']
SERVO_COUNT = len(JOINT_NAMES)

# Parameters that are a per-joint list and can be re-tuned at runtime
_TUNABLE_LISTS = ['min_angle', 'max_angle', 'invert', 'offset']


class RoboArmNode(Node):
    """
    ROS2 node controlling the 5-DOF robotic arm.
    """

    def __init__(self):
        super().__init__('roboarm_node')

        # ── PCA9685 board ───────────────────────────────────────────────
        self.declare_parameter('i2c_bus', 1)
        self.declare_parameter('i2c_address', 0x40)
        self.declare_parameter('servo_freq', 50)
        self.declare_parameter('channels', [0, 1, 2, 3, 4])

        # ── Per-joint calibration ───────────────────────────────────────
        self.declare_parameter('min_angle', [0.0] * SERVO_COUNT)
        self.declare_parameter('max_angle', [180.0] * SERVO_COUNT)
        self.declare_parameter('min_pulse', [750] * SERVO_COUNT)
        self.declare_parameter('max_pulse', [2250] * SERVO_COUNT)
        self.declare_parameter('actuation_range', [180] * SERVO_COUNT)
        self.declare_parameter('invert', [False] * SERVO_COUNT)
        self.declare_parameter('offset', [0.0] * SERVO_COUNT)

        # ── Motion safety ───────────────────────────────────────────────
        self.declare_parameter('max_step_deg', 0.0)
        self.declare_parameter('apply_default_pose', False)
        self.declare_parameter('default_pose', [90.0] * SERVO_COUNT)
        self.declare_parameter('release_on_shutdown', True)

        self.i2c_bus = self._int_param('i2c_bus')
        self.i2c_address = self._int_param('i2c_address')
        self.servo_freq = self._int_param('servo_freq')
        self.channels = self._list_param('channels', int, list(range(SERVO_COUNT)))

        self.min_angle = self._list_param('min_angle', float, [0.0] * SERVO_COUNT)
        self.max_angle = self._list_param('max_angle', float, [180.0] * SERVO_COUNT)
        self.min_pulse = self._list_param('min_pulse', int, [750] * SERVO_COUNT)
        self.max_pulse = self._list_param('max_pulse', int, [2250] * SERVO_COUNT)
        self.actuation_range = self._list_param(
            'actuation_range', int, [180] * SERVO_COUNT)
        self.invert = self._list_param('invert', bool, [False] * SERVO_COUNT)
        self.offset = self._list_param('offset', float, [0.0] * SERVO_COUNT)

        self.max_step_deg = float(self.get_parameter('max_step_deg').value)
        self.release_on_shutdown = bool(
            self.get_parameter('release_on_shutdown').value)

        # Last angle actually written to each servo (None until first command)
        self.servo_angles = [None] * SERVO_COUNT

        # Servo driver (None when hardware is unavailable → dry-run mode)
        self.kit = self._setup_servo_driver()

        self.add_on_set_parameters_callback(self._on_set_parameters)

        self.sub_servo = self.create_subscription(
            Float32MultiArray,
            '/wicom/servo_control',
            self._servo_callback,
            10
        )

        self.get_logger().info(
            '──────────────────────────────────────────────\n'
            '  wicom_roboarm_ros2 - Robotic Arm Node (ROS2)\n'
            '  Listening on topic: /wicom/servo_control\n'
            f'  PCA9685: bus={self.i2c_bus}, '
            f'address=0x{self.i2c_address:02X}, freq={self.servo_freq}Hz\n'
            f'  Driver: {"HARDWARE" if self.kit else "DRY-RUN (log only)"}\n'
            f'  Limits: ' + ', '.join(
                f'{JOINT_NAMES[i]} {self.min_angle[i]:.0f}-{self.max_angle[i]:.0f}°'
                for i in range(SERVO_COUNT)) + '\n'
            f'  Max step: '
            f'{"unlimited" if self.max_step_deg <= 0 else f"{self.max_step_deg:.0f}°/cmd"}\n'
            '──────────────────────────────────────────────'
        )

        if self.get_parameter('apply_default_pose').value:
            pose = self._list_param('default_pose', float, [90.0] * SERVO_COUNT)
            self.get_logger().warn(f'Applying default pose: {pose}')
            self._apply_angles(pose)
        else:
            self.get_logger().info(
                'Default pose disabled — servos stay where they are until the '
                'first command arrives.')

    # ─── Parameter helpers ──────────────────────────────────────────────

    def _int_param(self, name):
        return int(self.get_parameter(name).value)

    def _list_param(self, name, cast, fallback):
        """Read a per-joint list parameter, padded/truncated to SERVO_COUNT."""
        value = self.get_parameter(name).value
        if value is None:
            return list(fallback)

        values = [cast(v) for v in value]
        if len(values) < SERVO_COUNT:
            self.get_logger().warn(
                f"Parameter '{name}' has {len(values)} entries, expected "
                f'{SERVO_COUNT} — padding with defaults.')
            values += list(fallback[len(values):])
        return values[:SERVO_COUNT]

    def _on_set_parameters(self, params):
        """Allow re-tuning limits/direction at runtime via `ros2 param set`."""
        for p in params:
            if p.name in _TUNABLE_LISTS:
                values = list(p.value)
                if len(values) != SERVO_COUNT:
                    return SetParametersResult(
                        successful=False,
                        reason=f'{p.name} must have {SERVO_COUNT} entries')
                setattr(self, p.name, values)
                self.get_logger().info(f'Updated {p.name} = {values}')
            elif p.name == 'max_step_deg':
                self.max_step_deg = float(p.value)
                self.get_logger().info(f'Updated max_step_deg = {p.value}')
        return SetParametersResult(successful=True)

    # ─── Hardware setup ─────────────────────────────────────────────────

    def _setup_servo_driver(self):
        """
        Create the PCA9685 ServoKit and calibrate each channel. Returns None
        (dry-run) when the libraries or the I2C bus are unavailable, so the
        node still runs on a machine without the arm attached.
        """
        try:
            from adafruit_servokit import ServoKit
        except ImportError:
            self.get_logger().warn(
                'adafruit-circuitpython-servokit not installed — running in '
                'dry-run mode. Install with: '
                'pip3 install adafruit-circuitpython-servokit')
            return None

        try:
            i2c = None
            try:
                # Lets us pick the I2C bus number (bus 1 on a Raspberry Pi)
                from adafruit_extended_bus import ExtendedI2C
                i2c = ExtendedI2C(self.i2c_bus)
            except ImportError:
                self.get_logger().info(
                    'adafruit-extended-bus not installed — falling back to '
                    'the default board I2C bus.')

            if i2c is not None:
                kit = ServoKit(channels=16, i2c=i2c, address=self.i2c_address)
            else:
                kit = ServoKit(channels=16, address=self.i2c_address)

            kit.frequency = self.servo_freq

            for i, channel in enumerate(self.channels):
                servo = kit.servo[channel]
                servo.actuation_range = self.actuation_range[i]
                servo.set_pulse_width_range(self.min_pulse[i], self.max_pulse[i])

            return kit
        except Exception as e:
            self.get_logger().error(
                f'Failed to initialise PCA9685 ({e}) — running in dry-run '
                f'mode. Check that I2C is enabled and the board is wired.')
            return None

    # ─── Command handling ───────────────────────────────────────────────

    def _servo_callback(self, msg):
        """
        Handle servo angle commands: [base, shoulder, elbow, wrist, gripper].
        Shorter arrays leave the remaining joints untouched.
        """
        if not msg.data:
            return

        angles = list(msg.data[:SERVO_COUNT])
        self.get_logger().debug(
            'Received servo command: ' +
            ', '.join(f'{JOINT_NAMES[i]}={a:.1f}'
                      for i, a in enumerate(angles)))

        self._apply_angles(angles)

    def _apply_angles(self, angles):
        """Calibrate, clamp and write the angles to the servos."""
        for i, raw in enumerate(angles):
            angle = self._calibrate(i, float(raw))

            if angle != raw:
                self.get_logger().debug(
                    f'{JOINT_NAMES[i]}: {raw:.1f}° → {angle:.1f}° after '
                    f'calibration/limits')

            self.servo_angles[i] = angle

            if self.kit is None:
                continue

            try:
                self.kit.servo[self.channels[i]].angle = angle
            except Exception as e:
                self.get_logger().error(
                    f'Failed to drive servo {self.channels[i]} '
                    f'({JOINT_NAMES[i]}): {e}')

    def _calibrate(self, i, angle):
        """offset → invert → angle limits → step limit."""
        angle += self.offset[i]

        if self.invert[i]:
            angle = self.actuation_range[i] - angle

        angle = min(max(angle, self.min_angle[i]), self.max_angle[i])

        if self.max_step_deg > 0.0 and self.servo_angles[i] is not None:
            previous = self.servo_angles[i]
            delta = angle - previous
            if abs(delta) > self.max_step_deg:
                angle = previous + self.max_step_deg * (1 if delta > 0 else -1)
                self.get_logger().warn(
                    f'{JOINT_NAMES[i]}: step limited to {self.max_step_deg:.0f}°'
                    f' (target was {delta:+.1f}° away)')

        return angle

    # ─── Cleanup ────────────────────────────────────────────────────────

    def destroy_node(self):
        # Runs on Ctrl+C and on SIGTERM (`systemctl stop`), so the servos are
        # always released instead of being left holding torque.
        if self.kit is not None and self.release_on_shutdown:
            for channel in self.channels:
                try:
                    self.kit.servo[channel].angle = None  # stop sending PWM
                except Exception:
                    pass
            try:
                self.get_logger().info('Servos released (PWM stopped).')
            except Exception:
                pass
        super().destroy_node()


def main(args=None):
    rclpy.init(args=args)
    node = RoboArmNode()

    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        # ExternalShutdownException is what SIGTERM looks like from rclpy
        node.get_logger().info('Shutting down Robotic Arm Node...')
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
