"""
Unity ↔ ROS 2 cho canh tay robot.

Chay 2 node:
  udp_server_node    — nhan goi UAVLink tu Unity, publish /wicom/servo_control
  servo_bridge_node  — doi sang JointState cho driver /pca9685_servo/command

Driver phan cung (wicom_roboarm_unified) chay RIENG, khong dung toi o day:
  ros2 launch wicom_roboarm wicom_roboarm.launch.py

Usage:
  ros2 launch wicom_roboarm_ros2 wicom_arm_full.launch.py
  ros2 launch wicom_roboarm_ros2 wicom_arm_full.launch.py udp_port:=14550
"""

import os

from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def generate_launch_description():
    pkg_share = get_package_share_directory('wicom_roboarm_ros2')
    bridge_params = os.path.join(pkg_share, 'config', 'servo_bridge.yaml')

    return LaunchDescription([
        DeclareLaunchArgument(
            'udp_port', default_value='14551',
            description='UDP port the Unity VR app sends UAVLink messages to'),
        DeclareLaunchArgument(
            'send_rate_hz', default_value='20.0',
            description='Rate (Hz) telemetry is sent back to Unity'),
        DeclareLaunchArgument(
            'bridge_params', default_value=bridge_params,
            description='YAML mapping Unity servo values to arm joints'),

        Node(
            package='wicom_udp_ros2',
            executable='udp_server_node',
            name='udp_server_node',
            output='screen',
            emulate_tty=True,
            parameters=[{
                'udp_port': LaunchConfiguration('udp_port'),
                'send_rate_hz': LaunchConfiguration('send_rate_hz'),
            }],
        ),

        Node(
            package='wicom_roboarm_ros2',
            executable='servo_bridge_node',
            name='servo_bridge_node',
            output='screen',
            emulate_tty=True,
            parameters=[LaunchConfiguration('bridge_params')],
        ),
    ])
