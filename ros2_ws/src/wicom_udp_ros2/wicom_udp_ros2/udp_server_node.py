#!/usr/bin/env python3
"""
UDP Server Node for Virtual-Copilot-UAM (ROS2 Humble)

This node bridges Unity VR app ↔ ROS2 topics via the custom UAVLink UDP protocol.
It replaces the ROS1 wicom_udp package.

Protocol:
  - Receives UAVLink binary messages from Unity over UDP
  - Decodes them and publishes corresponding ROS2 topics / service calls
  - Subscribes to ROS2 topics and sends UAVLink messages back to Unity

UAVLink Message Format:
  [MsgID (1 byte)] [Length (1 byte)] [Payload (N bytes)]

Message IDs (matching Unity's Uavlink_message_t.cs):
  0x01 = STATE             (Drone → Unity)
  0x05 = COMMAND           (Unity → Drone)
  0x06 = MANUAL_CONTROL    (Unity → Drone)
  0x08 = POSITION_CONTROL  (Unity → Drone)
  0x09 = POSITION_FEEDBACK (Drone → Unity)
  0x0A = DRONE_STATUS      (Drone → Unity)
  0x0B = RC_CHANNELS       (Unity → Drone)
  0x0C = VELOCITY_CONTROL  (Unity → Drone)
  0x0D = SERVO_CONTROL     (Unity → Drone)

Command IDs:
  22 = TAKEOFF
  23 = ARM
  24 = LAND
  26 = SETMODE
  27 = POSITION_CONTROL_MODE
  28 = CIRCLE
  29 = TRAIN_TOGGLE
  30 = VX_OVERRIDE
"""

import socket
import struct
import threading
import math
import time

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy

from geometry_msgs.msg import Twist, PoseStamped
from std_msgs.msg import Float32, Bool, Int32, UInt8


# ─── UAVLink Protocol Constants ────────────────────────────────────────────

# Message IDs (must match Unity's MessageId enum)
MSG_ID_STATE             = 0x01
MSG_ID_COMMAND           = 0x05
MSG_ID_MANUAL_CONTROL    = 0x06
MSG_ID_POSITION_CONTROL  = 0x08
MSG_ID_POSITION_FEEDBACK = 0x09
MSG_ID_DRONE_STATUS      = 0x0A
MSG_ID_RC_CHANNELS       = 0x0B
MSG_ID_VELOCITY_CONTROL  = 0x0C
MSG_ID_SERVO_CONTROL     = 0x0D

# Command IDs (must match Unity's CommandId enum)
CMD_TAKEOFF              = 22
CMD_ARM                  = 23
CMD_LAND                 = 24
CMD_SETMODE              = 26
CMD_POSITION_CONTROL_MODE = 27
CMD_CIRCLE               = 28
CMD_TRAIN_TOGGLE         = 29
CMD_VX_OVERRIDE          = 30

# Flight Modes (must match Unity's Telemetry.Mode enum)
MODE_MANUAL    = 0
MODE_ALTCTL    = 1
MODE_POSCTL    = 2
MODE_OFFBOARD  = 3
MODE_AUTO_LAND = 4


class UdpServerNode(Node):
    """
    ROS2 node that acts as a UDP bridge between the Unity VR app
    and the drone's ROS2 system.
    """

    def __init__(self):
        super().__init__('udp_server_node')

        # ── Declare parameters ──────────────────────────────────────────
        self.declare_parameter('udp_port', 14550)
        self.declare_parameter('send_rate_hz', 20.0)

        # Alt-hold parameters (for altitude controller)
        self.declare_parameter('alt_hold.enable', True)
        self.declare_parameter('alt_hold.target_z', 0.8)
        self.declare_parameter('alt_hold.kp', 1.0)
        self.declare_parameter('alt_hold.kd', 0.6)
        self.declare_parameter('alt_hold.max_vz', 0.5)

        # Vx override parameter
        self.declare_parameter('vx_override.enable', False)

        # ── Read parameters ─────────────────────────────────────────────
        self.udp_port = self.get_parameter('udp_port').value
        self.send_rate_hz = self.get_parameter('send_rate_hz').value
        self.alt_hold_enable = self.get_parameter('alt_hold.enable').value
        self.alt_hold_target_z = self.get_parameter('alt_hold.target_z').value
        self.alt_hold_kp = self.get_parameter('alt_hold.kp').value
        self.alt_hold_kd = self.get_parameter('alt_hold.kd').value
        self.alt_hold_max_vz = self.get_parameter('alt_hold.max_vz').value
        self.vx_override_enable = self.get_parameter('vx_override.enable').value

        # ── Drone state (will be updated by ROS2 subscriptions) ─────────
        self.drone_armed = 0
        self.drone_mode = MODE_POSCTL
        self.drone_connected = 0
        self.drone_battery = 0

        self.drone_lat = 0.0
        self.drone_lon = 0.0
        self.drone_alt = 0.0
        self.drone_pos_x = 0.0
        self.drone_pos_y = 0.0
        self.drone_pos_z = 0.0
        self.drone_vx = 0.0
        self.drone_vy = 0.0
        self.drone_vz = 0.0
        self.drone_roll = 0.0
        self.drone_pitch = 0.0
        self.drone_yaw = 0.0
        self.drone_short_dist = 0.0
        self.drone_long_dist = 0.0

        # Offboard position feedback
        self.pos_feedback_success = 0
        self.pos_feedback_error_x = 0.0
        self.pos_feedback_error_y = 0.0
        self.pos_feedback_error_z = 0.0

        # Unity client address (set when first message received)
        self.unity_addr = None
        self.unity_addr_lock = threading.Lock()

        # ── QoS profiles ────────────────────────────────────────────────
        sensor_qos = QoSProfile(
            reliability=ReliabilityPolicy.BEST_EFFORT,
            history=HistoryPolicy.KEEP_LAST,
            depth=10
        )
        reliable_qos = QoSProfile(
            reliability=ReliabilityPolicy.RELIABLE,
            history=HistoryPolicy.KEEP_LAST,
            depth=10
        )

        # ── Publishers (Unity → Drone commands) ─────────────────────────
        self.pub_manual_control = self.create_publisher(
            Twist, '/wicom/manual_control', reliable_qos)
        self.pub_position_setpoint = self.create_publisher(
            PoseStamped, '/wicom/position_setpoint', reliable_qos)
        self.pub_velocity_setpoint = self.create_publisher(
            Twist, '/wicom/velocity_setpoint', reliable_qos)
        self.pub_arm_cmd = self.create_publisher(
            Bool, '/wicom/cmd/arm', reliable_qos)
        self.pub_mode_cmd = self.create_publisher(
            Int32, '/wicom/cmd/set_mode', reliable_qos)
        self.pub_land_cmd = self.create_publisher(
            Bool, '/wicom/cmd/land', reliable_qos)
        self.pub_takeoff_cmd = self.create_publisher(
            Float32, '/wicom/cmd/takeoff', reliable_qos)
        self.pub_servo_control = self.create_publisher(
            Float32, '/wicom/servo_control', reliable_qos)
        self.pub_offboard_enable = self.create_publisher(
            Bool, '/wicom/cmd/offboard_enable', reliable_qos)
        self.pub_circle_cmd = self.create_publisher(
            Bool, '/wicom/cmd/circle', reliable_qos)
        self.pub_train_toggle = self.create_publisher(
            Bool, '/wicom/cmd/train_toggle', reliable_qos)

        # ── Subscribers (Drone → Unity telemetry) ───────────────────────
        # TODO: Replace these with actual PX4/MAVROS2 topics
        # These are placeholder topics — adapt to your flight controller setup
        #
        # If using MAVROS2:
        #   /mavros/state → drone state
        #   /mavros/local_position/pose → position
        #   /mavros/global_position/global → GPS
        #   /mavros/battery → battery
        #
        # If using PX4-ROS2 bridge (XRCE-DDS):
        #   /fmu/out/vehicle_status → drone state
        #   /fmu/out/vehicle_local_position → position
        #   /fmu/out/vehicle_global_position → GPS
        #   /fmu/out/battery_status → battery

        self.get_logger().info(
            '──────────────────────────────────────────────\n'
            '  wicom_udp_ros2 - UDP Server Node (ROS2)\n'
            f'  Listening on UDP port: {self.udp_port}\n'
            f'  Send rate: {self.send_rate_hz} Hz\n'
            f'  Alt-hold: {"ENABLED" if self.alt_hold_enable else "DISABLED"}\n'
            '──────────────────────────────────────────────'
        )

        # ── Start UDP socket ────────────────────────────────────────────
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEPORT, 1)
        except (AttributeError, OSError):
            pass
        self.sock.bind(('0.0.0.0', self.udp_port))
        self.sock.settimeout(0.1)  # Non-blocking with 100ms timeout

        # ── Start receive thread ────────────────────────────────────────
        self.running = True
        self.recv_thread = threading.Thread(
            target=self._recv_loop, daemon=True)
        self.recv_thread.start()

        # ── Timer for sending telemetry back to Unity ───────────────────
        period = 1.0 / self.send_rate_hz
        self.send_timer = self.create_timer(period, self._send_telemetry)

    # ═══════════════════════════════════════════════════════════════════════
    #  UDP RECEIVE LOOP
    # ═══════════════════════════════════════════════════════════════════════

    def _recv_loop(self):
        """Background thread: receive and decode UAVLink messages from Unity."""
        while self.running:
            try:
                data, addr = self.sock.recvfrom(1024)
                if len(data) < 2:
                    continue

                # Remember Unity's address for sending telemetry back
                with self.unity_addr_lock:
                    self.unity_addr = addr

                # Decode UAVLink message
                msg_id = data[0]
                msg_len = data[1]
                payload = data[2:2 + msg_len]

                self._handle_message(msg_id, payload)

            except socket.timeout:
                continue
            except Exception as e:
                self.get_logger().error(f'UDP recv error: {e}')
                time.sleep(0.1)

    def _handle_message(self, msg_id, payload):
        """Route received UAVLink message to the appropriate handler."""
        if msg_id == MSG_ID_COMMAND:
            self._on_command(payload)
        elif msg_id == MSG_ID_MANUAL_CONTROL:
            self._on_manual_control(payload)
        elif msg_id == MSG_ID_POSITION_CONTROL:
            self._on_position_control(payload)
        elif msg_id == MSG_ID_VELOCITY_CONTROL:
            self._on_velocity_control(payload)
        elif msg_id == MSG_ID_RC_CHANNELS:
            self._on_rc_channels(payload)
        elif msg_id == MSG_ID_SERVO_CONTROL:
            self._on_servo_control(payload)
        else:
            self.get_logger().warn(f'Unknown message ID: 0x{msg_id:02X}')

    # ─── Command Handler ────────────────────────────────────────────────

    def _on_command(self, payload):
        """Handle COMMAND messages (takeoff, arm, land, setmode, etc.)."""
        if len(payload) < 2:
            return

        cmd_id = struct.unpack_from('<H', payload, 0)[0]

        if cmd_id == CMD_ARM:
            if len(payload) >= 3:
                arm_value = payload[2]
                msg = Bool()
                msg.data = bool(arm_value)
                self.pub_arm_cmd.publish(msg)
                self.get_logger().info(
                    f'CMD ARM: {"ARM" if arm_value else "DISARM"}')

        elif cmd_id == CMD_TAKEOFF:
            if len(payload) >= 6:
                altitude = struct.unpack_from('<f', payload, 2)[0]
                msg = Float32()
                msg.data = altitude
                self.pub_takeoff_cmd.publish(msg)
                self.get_logger().info(f'CMD TAKEOFF: alt={altitude}m')

        elif cmd_id == CMD_LAND:
            msg = Bool()
            msg.data = True
            self.pub_land_cmd.publish(msg)
            self.get_logger().info('CMD LAND')

        elif cmd_id == CMD_SETMODE:
            if len(payload) >= 4:
                mode = payload[2]
                msg = Int32()
                msg.data = int(mode)
                self.pub_mode_cmd.publish(msg)
                self.get_logger().info(f'CMD SETMODE: {mode}')

        elif cmd_id == CMD_POSITION_CONTROL_MODE:
            if len(payload) >= 6:
                enable = struct.unpack_from('<f', payload, 2)[0]
                msg = Bool()
                msg.data = enable > 0.5
                self.pub_offboard_enable.publish(msg)
                self.get_logger().info(
                    f'CMD OFFBOARD: {"ENABLE" if msg.data else "DISABLE"}')

        elif cmd_id == CMD_CIRCLE:
            if len(payload) >= 6:
                enable = struct.unpack_from('<f', payload, 2)[0]
                msg = Bool()
                msg.data = enable > 0.5
                self.pub_circle_cmd.publish(msg)
                self.get_logger().info(
                    f'CMD CIRCLE: {"START" if msg.data else "STOP"}')

        elif cmd_id == CMD_TRAIN_TOGGLE:
            if len(payload) >= 6:
                enable = struct.unpack_from('<f', payload, 2)[0]
                msg = Bool()
                msg.data = enable > 0.5
                self.pub_train_toggle.publish(msg)
                self.get_logger().info(
                    f'CMD TRAIN: {"ENABLE" if msg.data else "DISABLE"}')

        elif cmd_id == CMD_VX_OVERRIDE:
            if len(payload) >= 6:
                enable = struct.unpack_from('<f', payload, 2)[0]
                self.vx_override_enable = enable > 0.5
                self.get_logger().info(
                    f'CMD VX_OVERRIDE: '
                    f'{"ENABLE" if self.vx_override_enable else "DISABLE"}')

        else:
            self.get_logger().warn(f'Unknown command ID: {cmd_id}')

    # ─── Manual Control Handler ─────────────────────────────────────────

    def _on_manual_control(self, payload):
        """
        Handle MANUAL_CONTROL messages.
        Payload: x(int32), y(int32), z(int32), r(int32) — each [-1000, 1000]
        Maps to: Roll, Pitch, Throttle, Yaw
        """
        if len(payload) < 16:
            return

        x, y, z, r = struct.unpack_from('<iiii', payload, 0)

        msg = Twist()
        msg.linear.x = float(x) / 1000.0   # Roll  → linear.x
        msg.linear.y = float(y) / 1000.0   # Pitch → linear.y
        msg.linear.z = float(z) / 1000.0   # Throttle → linear.z
        msg.angular.z = float(r) / 1000.0  # Yaw → angular.z

        self.pub_manual_control.publish(msg)

    # ─── Position Control Handler ───────────────────────────────────────

    def _on_position_control(self, payload):
        """
        Handle POSITION_CONTROL messages (Offboard mode).
        Payload: x(float), y(float), z(float), yaw(float), frame(byte)
        """
        if len(payload) < 17:
            return

        x, y, z, yaw = struct.unpack_from('<ffff', payload, 0)
        frame = payload[16]

        msg = PoseStamped()
        msg.header.stamp = self.get_clock().now().to_msg()
        msg.header.frame_id = 'map' if frame == 0 else 'base_link'
        msg.pose.position.x = float(x)
        msg.pose.position.y = float(y)
        msg.pose.position.z = float(z)

        # Convert yaw to quaternion (simplified: only yaw rotation)
        msg.pose.orientation.z = math.sin(yaw / 2.0)
        msg.pose.orientation.w = math.cos(yaw / 2.0)

        self.pub_position_setpoint.publish(msg)

    # ─── Velocity Control Handler ───────────────────────────────────────

    def _on_velocity_control(self, payload):
        """
        Handle VELOCITY_CONTROL messages (Offboard velocity mode).
        Payload: vx(float), vy(float), vz(float), yaw_rate(float), frame(byte)
        """
        if len(payload) < 17:
            return

        vx, vy, vz, yaw_rate = struct.unpack_from('<ffff', payload, 0)
        # frame = payload[16]  # 0: ENU local map, 1: body frame

        msg = Twist()
        msg.linear.x = float(vx)
        msg.linear.y = float(vy)
        msg.linear.z = float(vz)
        msg.angular.z = float(yaw_rate)

        self.pub_velocity_setpoint.publish(msg)

    # ─── RC Channels Handler ────────────────────────────────────────────

    def _on_rc_channels(self, payload):
        """
        Handle RC_CHANNELS messages.
        Payload: 8 x uint16 (chan1-chan8), PWM values [1000-2000]
        """
        if len(payload) < 16:
            return

        channels = struct.unpack_from('<HHHHHHHH', payload, 0)
        self.get_logger().debug(
            f'RC Channels: {channels}')
        # TODO: Publish to appropriate RC override topic if needed

    # ─── Servo Control Handler ──────────────────────────────────────────

    def _on_servo_control(self, payload):
        """
        Handle SERVO_CONTROL messages (Robotic Arm).
        Payload: servo0-servo4 (5 x float = 20 bytes)
        """
        if len(payload) < 20:
            return

        servos = struct.unpack_from('<fffff', payload, 0)
        self.get_logger().debug(
            f'Servo: base={servos[0]:.1f}, shoulder={servos[1]:.1f}, '
            f'elbow={servos[2]:.1f}, wrist={servos[3]:.1f}, '
            f'gripper={servos[4]:.1f}')
        # TODO: Forward to wicom_roboarm_ros2 node via topic or service

    # ═══════════════════════════════════════════════════════════════════════
    #  SEND TELEMETRY TO UNITY
    # ═══════════════════════════════════════════════════════════════════════

    def _send_telemetry(self):
        """Timer callback: send drone state + status to Unity via UDP."""
        with self.unity_addr_lock:
            addr = self.unity_addr

        if addr is None:
            return  # No Unity client connected yet

        try:
            # Send STATE message (0x01)
            self._send_state_msg(addr)

            # Send DRONE_STATUS message (0x0A)
            self._send_drone_status_msg(addr)

            # Send POSITION_FEEDBACK if in offboard mode
            if self.drone_mode == MODE_OFFBOARD:
                self._send_position_feedback_msg(addr)

        except Exception as e:
            self.get_logger().error(f'Send telemetry error: {e}')

    def _send_state_msg(self, addr):
        """
        Send STATE message to Unity.
        Payload: armed(int8), mode(int8), connected(int8), battery(int8)
        """
        payload = struct.pack('<bbbb',
                              self.drone_armed,
                              self.drone_mode,
                              self.drone_connected,
                              self.drone_battery)
        self._send_uavlink(MSG_ID_STATE, payload, addr)

    def _send_drone_status_msg(self, addr):
        """
        Send DRONE_STATUS message to Unity.
        Must match Unity's UavlinkDroneStatus.Decode() format:
        Altitude(float), Battery(sbyte), Latitude(double), Longitude(double),
        PosX(float), PosY(float), PosZ(float),
        VelX(float), VelY(float), VelZ(float),
        Roll(float), Pitch(float), Yaw(float),
        ShortDist(float), LongDist(float)
        """
        payload = struct.pack(
            '<fbddfffffffffff',
            self.drone_alt,
            self.drone_battery,
            self.drone_lat,
            self.drone_lon,
            self.drone_pos_x,
            self.drone_pos_y,
            self.drone_pos_z,
            self.drone_vx,
            self.drone_vy,
            self.drone_vz,
            self.drone_roll,
            self.drone_pitch,
            self.drone_yaw,
            self.drone_short_dist,
            self.drone_long_dist,
        )
        self._send_uavlink(MSG_ID_DRONE_STATUS, payload, addr)

    def _send_position_feedback_msg(self, addr):
        """
        Send POSITION_FEEDBACK message to Unity.
        Payload: success(byte), error_x(float), error_y(float), error_z(float)
        """
        payload = struct.pack(
            '<Bfff',
            self.pos_feedback_success,
            self.pos_feedback_error_x,
            self.pos_feedback_error_y,
            self.pos_feedback_error_z,
        )
        self._send_uavlink(MSG_ID_POSITION_FEEDBACK, payload, addr)

    def _send_uavlink(self, msg_id, payload, addr):
        """Encode and send a UAVLink message over UDP."""
        data = bytes([msg_id, len(payload)]) + payload
        self.sock.sendto(data, addr)

    # ═══════════════════════════════════════════════════════════════════════
    #  CLEANUP
    # ═══════════════════════════════════════════════════════════════════════

    def destroy_node(self):
        self.running = False
        if self.recv_thread.is_alive():
            self.recv_thread.join(timeout=2.0)
        self.sock.close()
        super().destroy_node()


def main(args=None):
    rclpy.init(args=args)
    node = UdpServerNode()

    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        node.get_logger().info('Shutting down UDP Server Node...')
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
