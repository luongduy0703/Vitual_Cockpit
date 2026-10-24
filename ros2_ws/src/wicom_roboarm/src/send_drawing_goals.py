#!/usr/bin/env python3
import rclpy
from rclpy.node import Node
import numpy as np
import math
import time
from sensor_msgs.msg import JointState
from geometry_msgs.msg import Point

# Precise URDF offsets for FK
JOINT_OFFSETS = [
    [-0.003394, -0.003955, 0.068502], # Base -> J1
    [0.041821, -0.019984, 0.053522],  # J1 -> J2
    [-0.075886, -7.0e-06, 0.116723],  # J2 -> J3
    [0.032204, 0.031535, 0.062164],   # J3 -> J4
    [-0.032579, -0.033100, 0.077214], # J4 -> J5
    [0.031600, 0.015300, 0.063800],   # J5 -> J6
    [0.000079, -0.016091, 0.046444]   # J6 -> EE
]

def rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])

def rot_y(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])

def rot_z(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])

def forward_kinematics(joint_angles):
    # Base link to Joint 1
    pos = np.array(JOINT_OFFSETS[0])
    R = rot_z(joint_angles[0])
    
    # Joint 2: -X-axis
    pos = pos + R @ JOINT_OFFSETS[1]
    R = R @ rot_x(-joint_angles[1])
    
    # Joint 3: -X-axis
    pos = pos + R @ JOINT_OFFSETS[2]
    R = R @ rot_x(-joint_angles[2])
    
    # Joint 4: -Y-axis
    pos = pos + R @ JOINT_OFFSETS[3]
    R = R @ rot_y(-joint_angles[3])
    
    # Joint 5: -X-axis
    pos = pos + R @ JOINT_OFFSETS[4]
    R = R @ rot_x(-joint_angles[4])
    
    # Joint 6: -Y-axis
    pos = pos + R @ JOINT_OFFSETS[5]
    R = R @ rot_y(-joint_angles[5])
    
    # Final EE offset
    pos = pos + R @ JOINT_OFFSETS[6]
    
    return pos

class SendDrawingGoals(Node):
    def __init__(self):
        super().__init__('send_drawing_goals')
        
        # Publishers & Subscribers
        self.pub_goal = self.create_publisher(Point, 'goal_point', 10)
        self.sub_joints = self.create_subscription(JointState, 'joint_states', self._joint_cb, 10)
        
        # Generate Square Waypoints in base_link (vertical plane at X = -0.50)
        # 6cm square centered at Y=0.0, Z=0.60
        corners = [
            np.array([-0.50, -0.03, 0.57]),
            np.array([-0.50,  0.03, 0.57]),
            np.array([-0.50,  0.03, 0.63]),
            np.array([-0.50, -0.03, 0.63])
        ]
        
        # Interpolate points along edges for smoother drawing
        self.waypoints = []
        steps_per_edge = 5
        for i in range(4):
            p1 = corners[i]
            p2 = corners[(i + 1) % 4]
            for step in range(steps_per_edge):
                alpha = step / float(steps_per_edge)
                self.waypoints.append(p1 + alpha * (p2 - p1))
                
        self.waypoint_index = 0
        self.tolerance = 0.015 # 1.5 cm tolerance to advance to next waypoint
        self.last_pub_time = 0.0
        self.loop_delay = 2.0  # Pause at the end of each square before repeating
        self.waiting_for_next_loop = False
        self.next_loop_start_time = 0.0
        
        # Timer to periodically publish the current goal point (keep-alive)
        self.create_timer(0.2, self._timer_cb)
        self.get_logger().info(f"Initialized SendDrawingGoals node. Created path with {len(self.waypoints)} waypoints.")

    def _joint_cb(self, msg: JointState):
        if len(msg.position) < 6:
            return
            
        current_joints = np.array(msg.position[:6])
        ee_pos = forward_kinematics(current_joints)
        
        if self.waiting_for_next_loop:
            if time.time() >= self.next_loop_start_time:
                self.waiting_for_next_loop = False
                self.waypoint_index = 0
                self.get_logger().info("🔄 Starting next square drawing loop!")
            return
            
        # Compute distance to current target
        target = self.waypoints[self.waypoint_index]
        dist = np.linalg.norm(target - ee_pos)
        
        if dist < self.tolerance:
            self.waypoint_index += 1
            if self.waypoint_index >= len(self.waypoints):
                self.get_logger().info("🎨 Square Drawing Loop Complete! Pausing...")
                self.waiting_for_next_loop = True
                self.next_loop_start_time = time.time() + self.loop_delay
            else:
                self.get_logger().info(f"✓ Waypoint {self.waypoint_index}/{len(self.waypoints)} reached. Next target: {self.waypoints[self.waypoint_index]}")
                self._publish_current_goal()

    def _publish_current_goal(self):
        if self.waiting_for_next_loop or self.waypoint_index >= len(self.waypoints):
            return
        target = self.waypoints[self.waypoint_index]
        msg = Point(x=float(target[0]), y=float(target[1]), z=float(target[2]))
        self.pub_goal.publish(msg)
        self.last_pub_time = time.time()

    def _timer_cb(self):
        # Periodically republish current goal to keep the RL node active
        self._publish_current_goal()

def main(args=None):
    rclpy.init(args=args)
    node = SendDrawingGoals()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.shutdown()

if __name__ == '__main__':
    main()
