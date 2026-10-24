#!/usr/bin/env python3
import rclpy
from rclpy.node import Node
from rclpy.rate import Rate
from sensor_msgs.msg import JointState

# Tên các khớp theo đúng thứ tự bạn đã gửi
JOINT_NAMES = ['base', 'shoulder', 'elbow', 'wrist_roll', 'wrist_pitch', 'pen']

# 22 điểm (J1 J2 J3 J4 J5 J6) lấy từ bảng bạn cung cấp
WAYPOINTS = [
    [90.28, 93.31, 42.25, 87.83, 124.51, 89.06],
    [92.11, 91.32, 37.72, 89.14, 129.22, 89.38],
    [93.93, 90.27, 33.81, 90.53, 133.10, 89.75],
    [95.76, 90.07, 30.39, 91.95, 136.23, 90.16],
    [97.61, 90.66, 27.41, 93.38, 138.64, 90.58],
    [99.47, 91.98, 24.84, 94.79, 140.34, 91.02],
    [101.33, 94.01, 22.64, 96.15, 141.32, 91.44],
    [103.19, 96.72, 20.85, 97.44, 141.60, 91.84],
    [99.43, 94.71, 19.57, 95.01, 143.84, 91.07],
    [95.56, 93.41, 18.76, 92.40, 145.31, 90.22],
    [91.63, 92.82, 18.39, 89.71, 145.95, 89.33],
    [87.67, 92.97, 18.49, 87.01, 145.78, 88.43],
    [83.73, 93.84, 19.02, 84.38, 144.76, 87.57],
    [79.85, 95.42, 20.00, 81.91, 142.92, 86.79],
    [76.05, 97.69, 21.44, 79.66, 140.32, 86.13],
    [77.92, 94.87, 23.15, 80.90, 140.22, 86.48],
    [79.78, 92.71, 25.26, 82.26, 139.43, 86.88],
    [81.62, 91.26, 27.78, 83.70, 137.94, 87.33],
    [83.43, 90.54, 30.69, 85.20, 135.74, 87.80],
    [85.19, 90.60, 34.03, 86.74, 132.81, 88.29],
    [86.93, 91.48, 37.84, 88.27, 129.12, 88.78],
    [88.63, 93.30, 42.25, 89.77, 124.58, 89.24],
]

TOPIC = '/pca9685_servo/command'
PUBLISH_RATE_HZ = 10  # tương đương -r 10
DURATION_S = 2.0      # tương đương -t 2

class WaypointPublisher(Node):
    def __init__(self):
        super().__init__('waypoint_publisher')
        self.pub = self.create_publisher(JointState, TOPIC, 10)
        self.get_logger().info(f'Publisher tạo trên topic: {TOPIC}')

    def send_waypoints(self, waypoints, names, rate_hz=10, duration_s=2.0):
        rate = Rate(rate_hz, self.get_clock())
        for idx, wp in enumerate(waypoints, start=1):
            # Tạo message JointState chứa tất cả 6 khớp (gửi "cùng lúc")
            msg = JointState()
            msg.header.stamp = self.get_clock().now().to_msg()
            msg.name = names
            msg.position = wp

            # In ra điểm và lệnh gửi từng khớp
            print(f'Point {idx}:')
            for n, p in zip(names, wp):
                print(f'  {n}: {p}')
            # Gửi lặp để đảm bảo nhận (tương tự ros2 topic pub -r 10 -t 2)
            loops = int(rate_hz * duration_s)
            for _ in range(loops):
                msg.header.stamp = self.get_clock().now().to_msg()
                self.pub.publish(msg)
                # sleep theo Rate của node
                try:
                    rate.sleep()
                except Exception:
                    # nếu node shutdown hoặc interrupt thì dừng sớm
                    return

def main(args=None):
    rclpy.init(args=args)
    node = WaypointPublisher()
    try:
        node.send_waypoints(WAYPOINTS, JOINT_NAMES, rate_hz=PUBLISH_RATE_HZ, duration_s=DURATION_S)
    except KeyboardInterrupt:
        node.get_logger().info('Bị ngắt bởi người dùng')
    finally:
        node.get_logger().info('Shutting down node...')
        node.destroy_node()
        rclpy.shutdown()

if __name__ == '__main__':
    main()
