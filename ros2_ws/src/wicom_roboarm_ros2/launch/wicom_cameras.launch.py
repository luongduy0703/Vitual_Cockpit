"""
Hai camera USB → hai man hinh trong kinh VR.

Scene VRCOPILOT da gan san URL cho hai topic:
    FrontCameraStream                  → /usb_camera/image_raw
    BottomCameraStream, SecondCamera…  → /main_camera/image_raw

Launch file nay chay hai node camera publish dung hai ten do, cong them
web_video_server bien anh thanh luong MJPEG tren cong 8080 — dung dinh dang
URL ma Vuplex WebView trong Unity dang doi.

Chay:
  ros2 launch wicom_roboarm_ros2 wicom_cameras.launch.py
  ros2 launch wicom_roboarm_ros2 wicom_cameras.launch.py width:=480 height:=360 fps:=10.0

NEN dung duong dan on dinh thay cho /dev/video0, vi so thu tu co the doi
sau moi lan khoi dong:
  ls /dev/v4l/by-id/
  ros2 launch wicom_roboarm_ros2 wicom_cameras.launch.py \
      main_device:=/dev/v4l/by-id/usb-XXXX-video-index0
"""

from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def camera(namespace, device, width, height, fps, pixel_format):
    """Mot node usb_cam publish ra <namespace>/image_raw."""
    return Node(
        package='usb_cam',
        executable='usb_cam_node_exe',
        name='usb_cam',
        namespace=namespace,
        output='screen',
        emulate_tty=True,
        parameters=[{
            'video_device': device,
            'image_width': width,
            'image_height': height,
            'framerate': fps,
            'pixel_format': pixel_format,
            'camera_name': namespace,
            # Khong can calib de xem hinh; tat canh bao thieu file calib
            'camera_info_url': '',
        }],
    )


def generate_launch_description():
    return LaunchDescription([
        DeclareLaunchArgument('main_device', default_value='/dev/video0',
                              description='Camera cho man hinh BottomCameraStream'),
        DeclareLaunchArgument('usb_device', default_value='/dev/video2',
                              description='Camera cho man hinh FrontCameraStream'),
        DeclareLaunchArgument('width', default_value='640'),
        DeclareLaunchArgument('height', default_value='480'),
        DeclareLaunchArgument('fps', default_value='15.0'),
        DeclareLaunchArgument('pixel_format', default_value='mjpeg2rgb',
                              description='mjpeg2rgb tiet kiem bang thong USB; '
                                          'doi sang yuyv2rgb neu camera khong ho tro'),
        DeclareLaunchArgument('port', default_value='8080',
                              description='Cong HTTP, phai khop URL trong scene Unity'),
        DeclareLaunchArgument('quality', default_value='50',
                              description='Chat luong JPEG 1-100; giam de bot giat'),

        camera('main_camera', LaunchConfiguration('main_device'),
               LaunchConfiguration('width'), LaunchConfiguration('height'),
               LaunchConfiguration('fps'), LaunchConfiguration('pixel_format')),

        camera('usb_camera', LaunchConfiguration('usb_device'),
               LaunchConfiguration('width'), LaunchConfiguration('height'),
               LaunchConfiguration('fps'), LaunchConfiguration('pixel_format')),

        Node(
            package='web_video_server',
            executable='web_video_server',
            name='web_video_server',
            output='screen',
            emulate_tty=True,
            parameters=[{
                'port': LaunchConfiguration('port'),
                'address': '0.0.0.0',
                'default_stream_type': 'mjpeg',
                'quality': LaunchConfiguration('quality'),
            }],
        ),
    ])
