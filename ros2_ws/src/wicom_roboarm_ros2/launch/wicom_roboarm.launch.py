"""
Launch file for wicom_roboarm_ros2 - Robotic Arm Control Node.

Calibration lives in config/roboarm_params.yaml — edit that file to match the
real arm before powering the servos.

Usage:
  ros2 launch wicom_roboarm_ros2 wicom_roboarm.launch.py
  ros2 launch wicom_roboarm_ros2 wicom_roboarm.launch.py params_file:=/path/to/my.yaml
"""

import os

from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def generate_launch_description():
    default_params = os.path.join(
        get_package_share_directory('wicom_roboarm_ros2'),
        'config', 'roboarm_params.yaml')

    return LaunchDescription([
        DeclareLaunchArgument(
            'params_file', default_value=default_params,
            description='YAML file holding the per-joint arm calibration'),

        Node(
            package='wicom_roboarm_ros2',
            executable='roboarm_node',
            name='roboarm_node',
            output='screen',
            emulate_tty=True,
            parameters=[LaunchConfiguration('params_file')],
        ),
    ])
