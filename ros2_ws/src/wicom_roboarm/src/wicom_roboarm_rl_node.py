#!/usr/bin/env python3
import rclpy
from rclpy.node import Node
import numpy as np
import onnxruntime as ort
import os
import math
import time
from sensor_msgs.msg import JointState
from geometry_msgs.msg import Point
from std_msgs.msg import Header

# =============================================================================
# KINEMATICS (Aligned with URDF New.xacro)
# =============================================================================

# Precise URDF offsets
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
    """
    FK implementation matching New.xacro.
    J2 = -X, J3 = +X, J5 = +X. (J3 & J5 flipped in URDF to match J2 behavior)
    """
    # Base link to Joint 1
    pos = np.array(JOINT_OFFSETS[0])
    R = rot_z(joint_angles[0])
    
    # Joint 2: -X-axis
    pos = pos + R @ JOINT_OFFSETS[1]
    R = R @ rot_x(-joint_angles[1])
    
    # Joint 3: -X-axis (Aligned with Joint 2)
    pos = pos + R @ JOINT_OFFSETS[2]
    R = R @ rot_x(-joint_angles[2])
    
    # Joint 4: -Y-axis
    pos = pos + R @ JOINT_OFFSETS[3]
    R = R @ rot_y(-joint_angles[3])
    
    # Joint 5: -X-axis (Aligned with Joint 2)
    pos = pos + R @ JOINT_OFFSETS[4]
    R = R @ rot_x(-joint_angles[4])
    
    # Joint 6: -Y-axis
    pos = pos + R @ JOINT_OFFSETS[5]
    R = R @ rot_y(-joint_angles[5])
    
    # Final EE offset
    pos = pos + R @ JOINT_OFFSETS[6]
    
    return pos

# =============================================================================
# RL DEPLOYMENT NODE
# =============================================================================

class RoboArmRLNode(Node):
    def __init__(self):
        super().__init__('wicom_roboarm_rl_node')
        
        # Parameters
        self.declare_parameter('actor_path', 'onnx_models/actor_drawing.onnx')
        self.declare_parameter('nik_path', 'onnx_models/neural_ik.onnx')
        self.declare_parameter('use_quantized', False)
        self.declare_parameter('control_rate_hz', 5.0)
        self.declare_parameter('waypoint_tolerance', 0.01) # 1cm
        self.declare_parameter('home_deg', 90.0) # For servo command mapping
        
        # Paths
        actor_p = self.get_parameter('actor_path').value
        nik_p = self.get_parameter('nik_path').value
        use_quantized = self.get_parameter('use_quantized').value
        
        if use_quantized:
            a_dir, a_file = os.path.split(actor_p)
            a_name, a_ext = os.path.splitext(a_file)
            actor_p = os.path.join(a_dir, f"{a_name}_quantized{a_ext}")
            
            n_dir, n_file = os.path.split(nik_p)
            n_name, n_ext = os.path.splitext(n_file)
            nik_p = os.path.join(n_dir, f"{n_name}_quantized{n_ext}")
        
        # 1. Try resolving via standard ament share index
        resolved_via_share = False
        try:
            from ament_index_python.packages import get_package_share_directory
            share_dir = get_package_share_directory('wicom_roboarm')
            share_actor = os.path.join(share_dir, actor_p)
            share_nik = os.path.join(share_dir, nik_p)
            if os.path.exists(share_actor) and os.path.exists(share_nik):
                actor_p = share_actor
                nik_p = share_nik
                resolved_via_share = True
        except Exception:
            pass
            
        # 2. Fallback to legacy/relative paths if not found in share directory
        if not resolved_via_share:
            deploy_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
            
            if not os.path.isabs(actor_p):
                actor_p_legacy = os.path.join(deploy_dir, actor_p)
                actor_p_source = os.path.join(deploy_dir, 'wicom_roboarm', actor_p)
                if os.path.exists(actor_p_source):
                    actor_p = actor_p_source
                else:
                    actor_p = actor_p_legacy
                    
            if not os.path.isabs(nik_p):
                nik_p_legacy = os.path.join(deploy_dir, nik_p)
                nik_p_source = os.path.join(deploy_dir, 'wicom_roboarm', nik_p)
                if os.path.exists(nik_p_source):
                    nik_p = nik_p_source
                else:
                    nik_p = nik_p_legacy
        
        # Load ONNX models
        self.get_logger().info(f"Loading Actor: {actor_p}")
        self.actor_session = ort.InferenceSession(actor_p)
        
        self.get_logger().info(f"Loading Neural IK: {nik_p}")
        self.nik_session = ort.InferenceSession(nik_p)
        
        # State
        self.current_joints = np.zeros(6)
        self.data_ready = False
        self.waypoints = []
        self.waypoint_index = 0
        self.total_waypoints = 0
        self.is_running = False
        
        # ROS 
        self.sub_joints = self.create_subscription(JointState, 'joint_states', self._joint_cb, 10)
        self.sub_goal = self.create_subscription(Point, 'goal_point', self._goal_cb, 10)
        self.pub_cmd = self.create_publisher(JointState, 'command', 10)
        
        self.timer = self.create_timer(1.0 / self.get_parameter('control_rate_hz').value, self._timer_cb)
        
        self.get_logger().info("RL Node Initialized. Send Point to 'goal_point' to start.")

    def _joint_cb(self, msg: JointState):
        if len(msg.position) >= 6:
            # RL uses radians. If the driver publishes degrees, we convert here.
            # Assuming JointState from Gazebo or real driver is in Radians as per ROS standard.
            # But the user's servos might be controlled in degrees.
            self.current_joints = np.array(msg.position[:6])
            self.data_ready = True

    def _goal_cb(self, msg: Point):
        self.waypoints = [np.array([msg.x, msg.y, msg.z])]
        self.waypoint_index = 0
        self.total_waypoints = 1
        self.is_running = True
        self.get_logger().info(f"New target: {msg.x:.2f}, {msg.y:.2f}, {msg.z:.2f}")

    def _timer_cb(self):
        if not self.data_ready or not self.is_running or self.waypoint_index >= self.total_waypoints:
            return

        ee_pos = forward_kinematics(self.current_joints)
        target = self.waypoints[self.waypoint_index]
        dist_xyz = target - ee_pos
        dist_3d = np.linalg.norm(dist_xyz)
        
        progress = self.waypoint_index / max(1, self.total_waypoints)
        remaining = float(self.total_waypoints - self.waypoint_index)
        
        state = np.concatenate([
            self.current_joints,
            ee_pos,
            target,
            dist_xyz,
            [dist_3d],
            [progress],
            [remaining]
        ]).astype(np.float32).reshape(1, 18)
        
        # Predict Action
        action = self.actor_session.run(None, {'input': state})[0][0]
        
        # Compute Waypoint
        direction = target - ee_pos
        if dist_3d > 0.001:
            dir_norm = direction / dist_3d
            step_size = (action[0] + 1) / 2 * 0.15 
            fine = action[1:3] * 0.02
            target_xyz = ee_pos + dir_norm * step_size
            target_xyz[0] += fine[0]
            target_xyz[2] += fine[1] if len(fine) > 1 else 0
        else:
            target_xyz = target
            
        # Predict Joints (Neural IK)
        target_xyz_in = target_xyz.astype(np.float32).reshape(1, 3)
        predicted_joints = self.nik_session.run(None, {'input': target_xyz_in})[0][0]
        
        # Command (Degrees)
        cmd_degs = [math.degrees(j) + self.get_parameter('home_deg').value for j in predicted_joints]
        
        cmd = JointState()
        cmd.header.stamp = self.get_clock().now().to_msg()
        cmd.name = ['base', 'shoulder', 'elbow', 'wrist_roll', 'wrist_pitch', 'pen']
        cmd.position = [float(v) for v in cmd_degs]
        self.pub_cmd.publish(cmd)
        
        if dist_3d < self.get_parameter('waypoint_tolerance').value:
            self.waypoint_index += 1
            if self.waypoint_index >= self.total_waypoints:
                self.is_running = False

def main():
    rclpy.init()
    node = RoboArmRLNode()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.shutdown()

if __name__ == "__main__":
    main()
