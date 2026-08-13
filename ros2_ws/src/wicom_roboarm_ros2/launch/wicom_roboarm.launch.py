"""
Launch file for wicom_roboarm_ros2 - Robotic Arm Control Node.

Usage:
  ros2 launch wicom_roboarm_ros2 wicom_roboarm.launch.py
"""

from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def generate_launch_description():
    return LaunchDescription([
        DeclareLaunchArgument(
            'i2c_bus', default_value='1',
            description='I2C bus number for PCA9685 servo driver'),
        DeclareLaunchArgument(
            'i2c_address', default_value='64',  # 0x40
            description='I2C address for PCA9685 servo driver'),

        Node(
            package='wicom_roboarm_ros2',
            executable='roboarm_node',
            name='roboarm_node',
            output='screen',
            parameters=[{
                'i2c_bus': LaunchConfiguration('i2c_bus'),
                'i2c_address': LaunchConfiguration('i2c_address'),
            }],
        ),
    ])
