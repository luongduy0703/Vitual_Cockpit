#!/usr/bin/env python3
"""
Interactive calibration console for the 5/6-DOF arm.

Drives one joint at a time straight into the arm driver
(/pca9685_servo/command), so you can find the servo angle where each joint
is physically aligned, mark it, and let the tool compute the offsets that
belong in servo_bridge.yaml.

Run it on the Pi:
    python3 ~/ros2_ws/src/wicom_roboarm_ros2/wicom_roboarm_ros2/arm_jog.py
or, once the package is rebuilt:
    ros2 run wicom_roboarm_ros2 arm_jog
"""

import difflib
import sys
import unicodedata

import rclpy
from rclpy.node import Node
from sensor_msgs.msg import JointState
from std_srvs.srv import Trigger

# Joints of the real arm, in the driver's order (servos.yaml)
JOINTS = ['base', 'shoulder', 'elbow', 'wrist_roll', 'wrist_pitch', 'pen']

# What Unity sends when the arm is fully extended (theta = 0 in its IK),
# per Unity index: [base, shoulder, elbow, wrist, gripper]
UNITY_STRAIGHT = {'base': 90.0, 'shoulder': 90.0, 'elbow': 180.0,
                  'wrist_pitch': 150.0, 'pen': 90.0}

# Which Unity index each joint is fed from (see servo_bridge.yaml)
UNITY_INDEX = {'base': 0, 'shoulder': 1, 'elbow': 2, 'wrist_pitch': 3, 'pen': 4}

# Short names, so a typo in "wrist_pitch" does not silently steer another joint
ALIASES = {
    'b': 'base', 'ba': 'base',
    's': 'shoulder', 'sh': 'shoulder', 'vai': 'shoulder',
    'e': 'elbow', 'el': 'elbow', 'khuyu': 'elbow',
    'wr': 'wrist_roll', 'roll': 'wrist_roll', 'wristroll': 'wrist_roll',
    'wp': 'wrist_pitch', 'pitch': 'wrist_pitch', 'wrist': 'wrist_pitch',
    'wristpitch': 'wrist_pitch',
    'p': 'pen', 'but': 'pen',
}


# A Vietnamese IME in Telex mode turns "w" into "ư", "d d" into "đ" and so
# on, so "wp" arrives as "ưp". Fold those back before parsing.
TELEX_FOLD = str.maketrans({
    'ư': 'w', 'Ư': 'w', 'ơ': 'o', 'Ơ': 'o', 'đ': 'd', 'Đ': 'd',
    'ă': 'a', 'Ă': 'a', 'â': 'a', 'Â': 'a', 'ê': 'e', 'Ê': 'e',
    'ô': 'o', 'Ô': 'o', '\ufffd': '',
})


def fold_input(text):
    """Strip accents and undo Telex substitutions so joint names still match."""
    folded = text.translate(TELEX_FOLD)
    # drop any remaining combining marks (dấu thanh)
    folded = ''.join(c for c in unicodedata.normalize('NFD', folded)
                     if not unicodedata.combining(c))
    return folded


def resolve_joint(token):
    """Map what the user typed to a joint name, or None."""
    t = fold_input(token).lower().replace('-', '_')
    if t in JOINTS:
        return t
    if t in ALIASES:
        return ALIASES[t]
    flat = t.replace('_', '')
    for j in JOINTS:
        if j.replace('_', '') == flat:
            return j
    # unique prefix, e.g. "should" or "elb"
    hits = [j for j in JOINTS if j.startswith(t)]
    if len(hits) == 1:
        return hits[0]
    return None


def suggest(token):
    """Closest joint names, for a typo like 'wirst_pitch'."""
    near = difflib.get_close_matches(token.lower(), JOINTS, n=2, cutoff=0.5)
    return near or JOINTS

HELP = """
─── Lenh ────────────────────────────────────────────────────────────
  <goc>              dua khop DANG CHON toi goc do        vd:  120
  +<so> / -<so>      nhich khop dang chon                 vd:  +5   -10
  <khop> <goc>       dua mot khop bat ky toi goc          vd:  elbow 70
  <khop>             chon khop khac                       vd:  wrist_pitch
  all <goc>          dua CA 6 khop toi cung mot goc       vd:  all 90
  mark               danh dau goc hien tai la "THANG HANG" cua khop dang chon
  table              xem cac moc da danh dau + offset tinh duoc + YAML
  off                ngat PWM, tha long toan bo servo
  ?                  in lai bang lenh nay
  q                  thoat
─── Khop (go day du hoac viet tat) ──────────────────────────────────
  base=b   shoulder=s   elbow=e   wrist_roll=wr   wrist_pitch=wp   pen=p
─────────────────────────────────────────────────────────────────────
"""


class ArmJog(Node):

    def __init__(self):
        super().__init__('arm_jog')
        self.pub = self.create_publisher(
            JointState, '/pca9685_servo/command', 10)
        self.cli_disable = self.create_client(Trigger, '/pca9685_servo/disable')
        self.angles = {j: 90.0 for j in JOINTS}
        self.marks = {}
        self.current = 'shoulder'

    def send(self, joint, angle):
        angle = max(0.0, min(180.0, float(angle)))
        msg = JointState()
        msg.header.stamp = self.get_clock().now().to_msg()
        msg.name = [joint]
        msg.position = [angle]
        self.pub.publish(msg)
        self.angles[joint] = angle
        print(f"   → {joint} = {angle:.1f}°")

    def send_all(self, angle):
        angle = max(0.0, min(180.0, float(angle)))
        msg = JointState()
        msg.header.stamp = self.get_clock().now().to_msg()
        msg.name = list(JOINTS)
        msg.position = [angle] * len(JOINTS)
        self.pub.publish(msg)
        for j in JOINTS:
            self.angles[j] = angle
        print(f"   → tat ca 6 khop = {angle:.1f}°")

    def disable(self):
        if not self.cli_disable.wait_for_service(timeout_sec=2.0):
            print("   !! khong goi duoc /pca9685_servo/disable (driver chay chua?)")
            return
        self.cli_disable.call_async(Trigger.Request())
        print("   → da ngat PWM, servo tha long")

    def table(self):
        print("\n  khop          goc hien tai   moc THANG    Unity duoi   offset")
        print("  " + "─" * 62)
        for j in JOINTS:
            mark = self.marks.get(j)
            uni = UNITY_STRAIGHT.get(j)
            if mark is None:
                off = "—"
                mk = "—"
            elif uni is None:
                off = "(Unity khong dieu khien)"
                mk = f"{mark:.0f}°"
            else:
                off = f"{mark - uni:+.0f}°"
                mk = f"{mark:.0f}°"
            uni_s = f"{uni:.0f}°" if uni is not None else "—"
            print(f"  {j:13} {self.angles[j]:8.0f}°   {mk:>9}   {uni_s:>10}   {off:>8}")

        offsets = []
        missing = []
        for j in ['base', 'shoulder', 'elbow', 'wrist_pitch', 'pen']:
            if j in self.marks:
                offsets.append(self.marks[j] - UNITY_STRAIGHT[j])
            else:
                offsets.append(0.0)
                missing.append(j)

        print("\n  Dan vao servo_bridge.yaml:")
        print("    offset: [" + ", ".join(f"{o:.1f}" for o in offsets) + "]")
        if missing:
            print(f"    (chua 'mark': {', '.join(missing)} — dang tam de 0.0)")
        if 'wrist_roll' in self.marks:
            print(f"\n  wrist_roll Unity khong dieu khien. Giu no o "
                  f"{self.marks['wrist_roll']:.0f}° bang lenh:")
            print(f"    ros2 topic pub --once /pca9685_servo/command "
                  f"sensor_msgs/msg/JointState \"{{name: ['wrist_roll'], "
                  f"position: [{self.marks['wrist_roll']:.1f}]}}\"")
        print()


def main(args=None):
    rclpy.init(args=args)
    node = ArmJog()
    rclpy.spin_once(node, timeout_sec=1.0)   # cho DDS bat tay

    # A stray non-UTF-8 byte from an IME must not kill the session
    try:
        sys.stdin.reconfigure(errors='replace')
    except Exception:
        pass

    print(HELP)
    print(f"  Khop dang chon: {node.current}\n")

    try:
        while True:
            try:
                raw = input(f"[{node.current} = {node.angles[node.current]:.0f}°] > ").strip()
            except EOFError:
                break
            except UnicodeDecodeError:
                print("   !! ky tu la tu bo go (bat tieng Viet?) — "
                      "chuyen sang ENG roi go lai")
                continue
            if not raw:
                continue

            raw = fold_input(raw).strip()
            if not raw:
                continue

            parts = raw.split()
            head = parts[0].lower()

            if head in ('q', 'quit', 'exit'):
                break
            if head in ('?', 'h', 'help'):
                print(HELP); continue
            if head == 'table':
                node.table(); continue
            if head == 'off':
                node.disable(); continue
            if head == 'mark':
                node.marks[node.current] = node.angles[node.current]
                print(f"   ✓ {node.current} THANG HANG tai "
                      f"{node.angles[node.current]:.0f}°")
                continue
            if head == 'all' and len(parts) == 2:
                node.send_all(float(parts[1])); continue

            if head.startswith(('+', '-')) and len(head) > 1:
                try:
                    node.send(node.current, node.angles[node.current] + float(head))
                except ValueError:
                    print("   ?? vd: +5  hoac  -10")
                continue

            joint = resolve_joint(head)
            if joint:
                node.current = joint
                if len(parts) == 2:
                    try:
                        node.send(joint, float(parts[1]))
                    except ValueError:
                        print(f"   !! '{parts[1]}' khong phai so")
                else:
                    print(f"   → dang chon {joint} "
                          f"(hien {node.angles[joint]:.0f}°)")
                continue

            try:
                node.send(node.current, float(head))
            except ValueError:
                # Looks like a word, not a number → almost certainly a typo
                print(f"   !! KHONG CO KHOP TEN '{head}'  →  y ban la: "
                      f"{' / '.join(suggest(head))} ?")
                print(f"      (khop dang chon van la '{node.current}' — "
                      f"go dung ten roi thu lai)")
            except Exception as e:
                print(f"   !! loi: {e}")
    except KeyboardInterrupt:
        pass
    finally:
        node.table()
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
