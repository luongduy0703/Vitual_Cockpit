#!/usr/bin/env python3
"""
Servo bridge: Unity servo commands → wicom_roboarm_unified driver.

The UDP server publishes the 5 angles Unity computes with its IK
([base, shoulder, elbow, wrist, gripper]) as a Float32MultiArray. The arm
driver on the Pi (wicom_roboarm_unified) instead expects a JointState in
degrees on /pca9685_servo/command, and drives a 6-joint arm whose channels
are not 0..4.

This node does the translation, so neither side has to change:

    /wicom/servo_control  (Float32MultiArray, 5 values)
              ↓
    /pca9685_servo/command  (JointState, named joints, degrees)

Which Unity index feeds which joint is set by `joint_names` — an empty name
drops that index (e.g. leave the gripper alone).
"""

import rclpy
from rcl_interfaces.msg import SetParametersResult
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from sensor_msgs.msg import JointState
from std_msgs.msg import Float32MultiArray

# The driver reads any |angle| < 6.3 as radians instead of degrees
RADIAN_AMBIGUITY_LIMIT = 6.3

# Calibration that can be retuned live with `ros2 param set`, so the arm can be
# adjusted while it is running instead of through rebuild/restart cycles.
_LIVE_PARAMS = ('offset', 'invert', 'min_angle', 'max_angle')


class ServoBridgeNode(Node):

    def __init__(self):
        super().__init__('servo_bridge_node')

        self.declare_parameter('input_topic', '/wicom/servo_control')
        self.declare_parameter('command_topic', '/pca9685_servo/command')

        # One entry per value Unity sends, in Unity's order:
        #   [base, shoulder, elbow, wrist, gripper]
        # The name must match joint_names in the driver's servos.yaml.
        # An empty string means "ignore this value".
        self.declare_parameter(
            'joint_names',
            ['base', 'shoulder', 'elbow', 'wrist_pitch', 'pen'])

        # Mechanical calibration. The arm driver has no notion of direction or
        # zero offset, so both are applied here, in this order:
        #     angle = raw + offset  →  invert (180 - angle)  →  clamp
        self.declare_parameter('offset', [0.0, 0.0, 0.0, 0.0, 0.0])
        self.declare_parameter('invert', [False, False, False, False, False])

        # Safety clamp applied before the command leaves this node. The driver
        # clamps again with its own limits — this is the first line of defence.
        self.declare_parameter('min_angle', [0.0, 0.0, 0.0, 0.0, 0.0])
        self.declare_parameter('max_angle', [180.0, 180.0, 180.0, 180.0, 180.0])

        self.input_topic = self.get_parameter('input_topic').value
        self.command_topic = self.get_parameter('command_topic').value
        self.joint_names = [str(n) for n in
                            self.get_parameter('joint_names').value]
        self.offset = [float(v) for v in self.get_parameter('offset').value]
        self.invert = [bool(v) for v in self.get_parameter('invert').value]
        self.min_angle = [float(v) for v in
                          self.get_parameter('min_angle').value]
        self.max_angle = [float(v) for v in
                          self.get_parameter('max_angle').value]

        self._warned_radian_range = False

        self.add_on_set_parameters_callback(self._on_set_parameters)

        self.pub_command = self.create_publisher(JointState, self.command_topic, 10)
        self.sub_servo = self.create_subscription(
            Float32MultiArray, self.input_topic, self._on_servo, 10)

        mapping = ', '.join(
            f'[{i}]→{n}{"(inv)" if i < len(self.invert) and self.invert[i] else ""}'
            f'{f"{self.offset[i]:+.0f}°" if i < len(self.offset) and self.offset[i] else ""}'
            for i, n in enumerate(self.joint_names) if n)
        self.get_logger().info(
            '──────────────────────────────────────────────\n'
            '  wicom_roboarm_ros2 - Servo Bridge\n'
            f'  In : {self.input_topic} (Float32MultiArray)\n'
            f'  Out: {self.command_topic} (JointState, degrees)\n'
            f'  Map: {mapping}\n'
            '──────────────────────────────────────────────'
        )

    def _on_set_parameters(self, params):
        """Apply calibration changes immediately, without restarting the node."""
        for p in params:
            if p.name not in _LIVE_PARAMS:
                continue
            values = list(p.value)
            if len(values) != len(self.joint_names):
                return SetParametersResult(
                    successful=False,
                    reason=f'{p.name} must have {len(self.joint_names)} entries')
            cast = bool if p.name == 'invert' else float
            setattr(self, p.name, [cast(v) for v in values])
            self.get_logger().info(f'Updated {p.name} = {getattr(self, p.name)}')
        return SetParametersResult(successful=True)

    def _on_servo(self, msg):
        if not msg.data:
            return

        out = JointState()
        out.header.stamp = self.get_clock().now().to_msg()

        for i, value in enumerate(msg.data):
            if i >= len(self.joint_names):
                break
            name = self.joint_names[i]
            if not name:
                continue

            angle = float(value)

            # The driver maps 0-180° linearly onto the joint's pulse range,
            # so flipping direction is a mirror about 90°.
            angle += self.offset[i] if i < len(self.offset) else 0.0
            if i < len(self.invert) and self.invert[i]:
                angle = 180.0 - angle

            lo = self.min_angle[i] if i < len(self.min_angle) else 0.0
            hi = self.max_angle[i] if i < len(self.max_angle) else 180.0
            angle = min(max(angle, lo), hi)

            if abs(angle) < RADIAN_AMBIGUITY_LIMIT and not self._warned_radian_range:
                self._warned_radian_range = True
                self.get_logger().warn(
                    f'{name}={angle:.1f}° is below {RADIAN_AMBIGUITY_LIMIT}° — the '
                    f'driver will read it as radians. Raise min_angle above '
                    f'{RADIAN_AMBIGUITY_LIMIT}° for that joint.')

            out.name.append(name)
            out.position.append(angle)

        if out.name:
            self.pub_command.publish(out)
            self.get_logger().debug(
                'Forwarded: ' + ', '.join(
                    f'{n}={p:.1f}°' for n, p in zip(out.name, out.position)))


def main(args=None):
    rclpy.init(args=args)
    node = ServoBridgeNode()
    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        node.get_logger().info('Shutting down Servo Bridge...')
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
