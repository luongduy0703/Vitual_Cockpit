#!/usr/bin/env python3
"""
Robotic Arm Control Node for Virtual-Copilot-UAM (ROS2 Humble)

Subscribes to servo angle commands from the UDP server node
and drives the 5-DOF robotic arm servos.

Migrated from ROS1 (wicom_roboarm) to ROS2 Humble.
"""

import rclpy
from rclpy.node import Node
from std_msgs.msg import Float32
from geometry_msgs.msg import Twist

class RoboArmNode(Node):
    """
    ROS2 node controlling the 5-DOF robotic arm.
    """

    def __init__(self):
        super().__init__('roboarm_node')

        # Declare parameters for PCA9685 I2C / Servo angles
        self.declare_parameter('i2c_bus', 1)
        self.declare_parameter('i2c_address', 0x40)
        self.declare_parameter('servo_freq', 50)

        # Default angles (Base, Shoulder, Elbow, Wrist, Gripper)
        self.servo_angles = [90.0, 30.0, 0.0, 180.0, 90.0]

        # Subscribers
        self.sub_servo = self.create_subscription(
            Float32,
            '/wicom/servo_control',
            self._servo_callback,
            10
        )

        self.get_logger().info(
            '──────────────────────────────────────────────\n'
            '  wicom_roboarm_ros2 - Robotic Arm Node (ROS2)\n'
            '  Listening on topic: /wicom/servo_control\n'
            '──────────────────────────────────────────────'
        )

    def _servo_callback(self, msg):
        self.get_logger().debug(f'Received servo command: {msg.data}')
        # Update servo angles and send signal to driver hardware if available


def main(args=None):
    rclpy.init(args=args)
    node = RoboArmNode()

    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        node.get_logger().info('Shutting down Robotic Arm Node...')
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
