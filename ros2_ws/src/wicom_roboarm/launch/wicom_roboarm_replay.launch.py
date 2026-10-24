from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node
from ament_index_python.packages import get_package_share_directory
import os


def generate_launch_description():
    pkg_share = get_package_share_directory("wicom_roboarm")
    servo_yaml = os.path.join(pkg_share, "config", "servos.yaml")

    replay_plan = LaunchConfiguration("replay_plan")
    episodes = LaunchConfiguration("episodes")
    replay_rate_hz = LaunchConfiguration("replay_rate_hz")
    tolerance_deg = LaunchConfiguration("tolerance_deg")
    publish_mode = LaunchConfiguration("publish_mode")
    stream_hz = LaunchConfiguration("stream_hz")
    move_time_sec = LaunchConfiguration("move_time_sec")
    deadband_deg = LaunchConfiguration("deadband_deg")
    log_dir = LaunchConfiguration("log_dir")

    unified = Node(
        package="wicom_roboarm",
        executable="wicom_roboarm_unified_node.py",
        name="wicom_roboarm_unified",
        output="screen",
        parameters=[servo_yaml],
        remappings=[
            ("joint_states", "/pca9685_servo/joint_states"),
            ("enable", "/pca9685_servo/enable"),
            ("disable", "/pca9685_servo/disable"),
            ("home", "/pca9685_servo/home"),
            ("command", "/pca9685_servo/command"),
            ("trajectory", "/pca9685_servo/trajectory"),
            ("hardware_status", "/pca9685_servo/hardware_status"),
        ],
    )

    replay = Node(
        package="wicom_roboarm",
        executable="pi_replay_executor_node.py",
        name="pi_replay_executor",
        output="screen",
        arguments=[
            "--plan", replay_plan,
            "--episodes", episodes,
            "--replay-rate-hz", replay_rate_hz,
            "--tolerance-deg", tolerance_deg,
            "--publish-mode", publish_mode,
            "--stream-hz", stream_hz,
            "--move-time-sec", move_time_sec,
            "--deadband-deg", deadband_deg,
            "--log-dir", log_dir,
            "--print-segments",
        ],
    )

    return LaunchDescription([
        DeclareLaunchArgument("replay_plan", description="Path to pi_replay_plan_v1 JSON on the Pi"),
        DeclareLaunchArgument("episodes", default_value="1"),
        DeclareLaunchArgument("replay_rate_hz", default_value="3.0"),
        DeclareLaunchArgument("tolerance_deg", default_value="2.0"),
        DeclareLaunchArgument("publish_mode", default_value="keyframe-scurve"),
        DeclareLaunchArgument("stream_hz", default_value="10.0"),
        DeclareLaunchArgument("move_time_sec", default_value="1.2"),
        DeclareLaunchArgument("deadband_deg", default_value="0.5"),
        DeclareLaunchArgument("log_dir", default_value="~/ros2_ws/replay_logs"),
        unified,
        replay,
    ])
