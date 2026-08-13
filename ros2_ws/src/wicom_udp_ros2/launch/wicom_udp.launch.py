"""
Launch file for wicom_udp_ros2 - UDP Server Node.

Usage:
  ros2 launch wicom_udp_ros2 wicom_udp.launch.py
  ros2 launch wicom_udp_ros2 wicom_udp.launch.py udp_port:=14550 send_rate_hz:=20.0
"""

from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def generate_launch_description():
    return LaunchDescription([
        # ── Launch Arguments ────────────────────────────────────────────
        DeclareLaunchArgument(
            'udp_port', default_value='14550',
            description='UDP port to listen on for Unity connections'),
        DeclareLaunchArgument(
            'send_rate_hz', default_value='20.0',
            description='Rate (Hz) to send telemetry back to Unity'),
        DeclareLaunchArgument(
            'alt_hold_enable', default_value='true',
            description='Enable altitude hold controller'),
        DeclareLaunchArgument(
            'alt_hold_target_z', default_value='0.8',
            description='Target altitude for alt-hold (meters)'),
        DeclareLaunchArgument(
            'alt_hold_kp', default_value='1.0',
            description='Proportional gain for alt-hold'),
        DeclareLaunchArgument(
            'alt_hold_kd', default_value='0.6',
            description='Derivative gain for alt-hold'),
        DeclareLaunchArgument(
            'alt_hold_max_vz', default_value='0.5',
            description='Max vertical velocity for alt-hold (m/s)'),
        DeclareLaunchArgument(
            'vx_override_enable', default_value='false',
            description='Enable Vx velocity override'),

        # ── UDP Server Node ─────────────────────────────────────────────
        Node(
            package='wicom_udp_ros2',
            executable='udp_server_node',
            name='udp_server_node',
            output='screen',
            parameters=[{
                'udp_port': LaunchConfiguration('udp_port'),
                'send_rate_hz': LaunchConfiguration('send_rate_hz'),
                'alt_hold.enable': LaunchConfiguration('alt_hold_enable'),
                'alt_hold.target_z': LaunchConfiguration('alt_hold_target_z'),
                'alt_hold.kp': LaunchConfiguration('alt_hold_kp'),
                'alt_hold.kd': LaunchConfiguration('alt_hold_kd'),
                'alt_hold.max_vz': LaunchConfiguration('alt_hold_max_vz'),
                'vx_override.enable': LaunchConfiguration('vx_override_enable'),
            }],
        ),
    ])
