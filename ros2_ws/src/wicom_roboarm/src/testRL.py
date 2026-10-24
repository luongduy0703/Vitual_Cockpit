#!/usr/bin/env python3
import time
import rclpy
from rclpy.node import Node
from sensor_msgs.msg import JointState

# Tên các khớp theo đúng thứ tự bạn đã gửi (J1..J6)
JOINT_NAMES = ['base', 'shoulder', 'elbow', 'wrist_roll', 'wrist_pitch', 'pen']

WAYPOINTS = [
    [90.28, 93.31, 42.25, 87.83, 34.51, 89.06],
    [92.11, 91.32, 37.72, 89.14, 39.22, 89.38],
    [93.93, 90.27, 33.81, 90.53, 43.10, 89.75],
    [95.76, 90.07, 30.39, 91.95, 46.23, 90.16],
    [97.61, 90.66, 27.41, 93.38, 48.64, 90.58],
    [99.47, 91.98, 24.84, 94.79, 50.34, 91.02],
    [101.33, 94.01, 22.64, 96.15, 51.32, 91.44],
    [103.19, 96.72, 20.85, 97.44, 51.60, 91.84],
    [99.43, 94.71, 19.57, 95.01, 53.84, 91.07],
    [95.56, 93.41, 18.76, 92.40, 55.31, 90.22],
    [91.63, 92.82, 18.39, 89.71, 55.95, 89.33],
    [87.67, 92.97, 18.49, 87.01, 55.78, 88.43],
    [83.73, 93.84, 19.02, 84.38, 54.76, 87.57],
    [79.85, 95.42, 20.00, 81.91, 52.92, 86.79],
    [76.05, 97.69, 21.44, 79.66, 50.32, 86.13],
    [77.92, 94.87, 23.15, 80.90, 50.22, 86.48],
    [79.78, 92.71, 25.26, 82.26, 49.43, 86.88],
    [81.62, 91.26, 27.78, 83.70, 47.94, 87.33],
    [83.43, 90.54, 30.69, 85.20, 45.74, 87.80],
    [85.19, 90.60, 34.03, 86.74, 42.81, 88.29],
    [86.93, 91.48, 37.84, 88.27, 39.12, 88.78],
    [88.63, 93.30, 42.25, 89.77, 39.58, 89.24],
]

TOPIC = '/pca9685_servo/command'
PUBLISH_RATE_HZ = 10  # tương đương -r 10
DURATION_S = 2.0      # tương đương -t 2

class WaypointPublisher(Node):
    def __init__(self):
        super().__init__('waypoint_publisher')
        # Chỉ dùng 1 publisher lên đúng topic của bạn
        self.pub = self.create_publisher(JointState, TOPIC, 10)
        self.get_logger().info(f'Publisher tạo trên topic: {TOPIC}')

    def publish_point_separate_msgs(self, wp, names, rate_hz=10, duration_s=2.0):
        """
        Gửi cho mỗi điểm 6 message riêng (mỗi message chỉ chứa 1 joint),
        nhưng gửi chúng "cùng lúc" bằng cách publish tuần tự trong vòng lặp ở tần số rate_hz
        trong duration_s giây — tương đương với chạy 6 lệnh ros2 topic pub đồng thời.
        """
        # Tạo 6 message (mỗi msg chỉ cho 1 joint) theo đúng thứ tự names
        msgs = []
        for nm, pos in zip(names, wp):
            m = JointState()
            m.name = [nm]
            m.position = [pos]
            msgs.append(m)

        # In ra điểm và lệnh từng khớp (theo yêu cầu)
        print('---')
        print(f'Point {self._current_point_index}:')
        for nm, pos in zip(names, wp):
            print(f'  {nm}: {pos}')

        loops = int(rate_hz * duration_s)
        sleep_dt = 1.0 / rate_hz
        for _ in range(loops):
            # publish tất cả 6 messages mỗi chu kỳ (gần như "cùng lúc")
            for m in msgs:
                m.header.stamp = self.get_clock().now().to_msg()
                self.pub.publish(m)
            # đảm bảo publish ở tần số yêu cầu
            time.sleep(sleep_dt)

def main(args=None):
    rclpy.init(args=args)
    node = WaypointPublisher()
    try:
        # gửi tuần tự từ điểm 1 đến điểm 22
        node._current_point_index = 0
        for idx, wp in enumerate(WAYPOINTS, start=1):
            node._current_point_index = idx
            node.publish_point_separate_msgs(wp, JOINT_NAMES, rate_hz=PUBLISH_RATE_HZ, duration_s=DURATION_S)
    except KeyboardInterrupt:
        node.get_logger().info('Bị ngắt bởi người dùng')
    finally:
        node.get_logger().info('Kết thúc và shutdown node...')
        node.destroy_node()
        rclpy.shutdown()

if __name__ == '__main__':
    main()
