# Virtual Copilot for UAM

A VR control interface for a UAV-mounted robotic arm. The operator wears a
Cardboard-style headset, draws in the air with a bare hand, and a 6-DOF arm on
the aircraft follows the fingertip in real time. Live camera feeds from the
aircraft are shown on floating screens inside the headset.

```
[Android phone + Cardboard headset]
  rear camera → hand tracking → "Point" gesture
       ↓ projected onto a virtual drawing board → (u, v)
  Unity: map to robot space → inverse kinematics → 4 joint angles
       ↓ UAVLink binary message 0x0D (20 bytes), UDP port 14551
[Raspberry Pi 4 — Ubuntu 22.04 — ROS 2 Humble]
  udp_server_node    decode → /wicom/servo_control
  servo_bridge_node  calibration → /pca9685_servo/command
  wicom_roboarm      trajectory interpolation → I2C
       ↓
[PCA9685] → 6 PWM channels → servos

  usb_cam ×2 → web_video_server (MJPEG, port 8080) → screens in VR
```

## 1. Requirements

| Side | Requirement |
|---|---|
| Development PC | Windows 10/11, [Unity Hub](https://unity.com/download) + Unity **2021.3.4f1** with Android Build Support |
| Headset | Any Cardboard-style viewer |
| Phone | Android with gyroscope and a rear camera (hand tracking uses the rear camera) |
| Onboard computer | Raspberry Pi 4, **64-bit** Ubuntu 22.04 Server, ROS 2 Humble |
| Arm electronics | PCA9685 16-channel PWM board on I2C bus 1 (address 0x40), separate 5–6 V ≥ 5 A servo supply |
| Cameras | Up to two USB UVC cameras |

> The Pi must be 64-bit arm64. ROS 2 Humble has no apt packages for 32-bit
> armhf, so a Raspberry Pi OS image will not work.

## 2. Repository layout

```
Assets/Script/
  RoboticArmControl/SimpleDrawingCanvas.cs   hand → UV → IK → joint angles
  RoboticArmControl/DrawingBoardPlacer.cs    puts the board within arm's reach in VR
  CameraStream/MjpegStreamViewer.cs          MJPEG viewer (works in Editor and on device)
  Communication/                             UAVLink protocol + UDP client
  CanvasManager/                             in-VR buttons
  SpeechControl/                             Vietnamese voice commands

ros2_ws/src/
  wicom_udp_ros2/        UDP ↔ ROS 2 bridge, decodes UAVLink
  wicom_roboarm_ros2/    servo bridge, calibration config, bench tools
  wicom_roboarm/         arm hardware driver (lives on the Pi, not in this repo)

ros2_ws/scripts/         setup_pi_ubuntu.sh · deploy_to_pi.sh · check_pi.sh
ros2_ws/systemd/         auto-start services
```

## 3. First-time setup

### 3.0 Get the code

```bash
git clone https://github.com/luongduy0703/Vitual_Cockpit.git
cd Vitual_Cockpit
```

The repository holds both sides of the system: the Unity project under
`Assets/`, and the ROS 2 packages under `ros2_ws/`. Only the ROS 2 packages are
deployed to the Pi — see §3.2.

### 3.1 Raspberry Pi

```bash
# From the development PC
scp ros2_ws/scripts/setup_pi_ubuntu.sh ros2_ws/scripts/check_pi.sh <user>@<pi-address>:~

# On the Pi — installs ROS 2 Humble, enables I2C, installs servo libraries
bash setup_pi_ubuntu.sh
sudo reboot
```

After rebooting, confirm the environment is complete:

```bash
bash check_pi.sh
```

It reports OS, architecture, ROS distro, I2C, PCA9685 detection, user groups,
network and workspace state, ending with a pass/warn/fail count. Fix every
`[FAIL]` before continuing.

### 3.2 Deploy the ROS 2 packages

```bash
# On the development PC
bash ros2_ws/scripts/deploy_to_pi.sh <user>@<pi-address>
```

The script backs up the existing packages on the Pi, syncs **only**
`wicom_udp_ros2` and `wicom_roboarm_ros2`, and rebuilds just those two — other
packages in the Pi's workspace are left untouched.

### 3.3 Calibrate the arm

Do this before the first powered run, with the servo horns detached.

```bash
# 1. Find which servo is on which PCA9685 channel
python3 ~/ros2_ws/src/wicom_roboarm_ros2/wicom_roboarm_ros2/channel_test.py --scan

# 2. Record the mapping in the driver config
nano ~/ros2_ws/src/wicom_roboarm/config/servos.yaml     # joint_names / channels

# 3. Jog each joint, mark where it is aligned, read off the offsets
ros2 run wicom_roboarm_ros2 arm_jog
```

`arm_jog` is an interactive console: type an angle, nudge with `+5`/`-5`, type
`mark` when the joint is physically straight, then `table` to print the
calibration ready to paste into `servo_bridge.yaml`.

Unity expresses the fully extended pose as `(base 90, shoulder 90, elbow 180,
wrist 150)`. If the hardware is mounted so that 90° on every servo is the
straight pose, the resulting offsets are `[0, 0, -90, -60, 0]`.

### 3.4 Unity

Open the project with Unity **2021.3.4f1**, load `Assets/Scenes/VRCOPILOT.unity`
and check these values on two objects:

| GameObject | Field | Meaning |
|---|---|---|
| `Communication` | `_hostname` | IP address of the Pi |
| `Communication` | `_port` | UDP port, must match the bridge (default 14551) |
| `DrawingCanvas` | `L1_cm` | shoulder → elbow length |
| `DrawingCanvas` | `L2_cm` | elbow → wrist length |
| `DrawingCanvas` | `L3_cm` | wrist → pen tip length |
| `DrawingCanvas` | `zPlane_cm` | distance from the arm base to the drawing plane |

`zPlane_cm` **must be smaller than `L1+L2+L3`**. Set it larger and the
workspace calculation collapses to zero: the app keeps sending commands, the
logs stay clean, and the arm never moves. The last number on the in-VR
`ServoDisplay` panel is the computed workspace size — it must not be `0.0`.

Then `File → Build Settings → Android → Build`, and install the APK:

```bash
adb install -r VRCopilot.apk
```

## 4. Running

Start the Pi first — the nodes must be listening before the app sends anything.

```bash
# Terminal 1 — arm driver
ros2 launch wicom_roboarm wicom_roboarm.launch.py

# Terminal 2 — UDP bridge + servo bridge
ros2 launch wicom_roboarm_ros2 wicom_arm_full.launch.py

# Terminal 3 — cameras (optional)
ros2 launch wicom_roboarm_ros2 wicom_cameras.launch.py

# Terminal 4 — move the arm to its rest pose before opening the app
ros2 topic pub --once /pca9685_servo/command sensor_msgs/msg/JointState \
  "{name: ['base','shoulder','elbow','wrist_roll','wrist_pitch','pen'], \
    position: [90.0,90.0,90.0,90.0,90.0,90.0]}"
```

Then on the phone: join the aircraft's Wi-Fi, open the app, grant camera
permission, tap **Show Draw**, insert the phone into the headset, and point
with one index finger over the virtual board.

Keep an emergency stop ready in a spare terminal:

```bash
python3 ~/ros2_ws/src/wicom_roboarm/scripts/pca9685_kill_all.py
```

To start everything automatically at boot instead:

```bash
bash ~/ros2_ws/systemd/install_service.sh
```

## 5. Tuning

Only the Unity-side values need an APK rebuild. Everything else is a config
change on the Pi.

| To change | Where | Rebuild APK? |
|---|---|---|
| Joint direction | `invert` in `servo_bridge.yaml` | no |
| Joint zero offset | `offset` in `servo_bridge.yaml` | no |
| Safe angle limits | `min_angle` / `max_angle` in `servo_bridge.yaml` | no |
| Motion smoothing | `default_moving_time_sec` in `servos.yaml` | no |
| Rest pose | `neutral_deg_by_joint` in `servos.yaml` | no |
| Servo pulse range | `pulse_us_*_by_joint` in `servos.yaml` | no |
| Workspace size, reach | `zPlane_cm`, `L1/L2/L3` in the scene | **yes** |
| Motion amplitude | gain factors in `SimpleDrawingCanvas.cs` | **yes** |
| Command rate | `SendInterval` in `SimpleDrawingCanvas.cs` | **yes** |

`offset`, `invert`, `min_angle` and `max_angle` can be retuned **while the
system is running**, which is the fastest way to converge on a good setup:

```bash
ros2 param set /servo_bridge_node offset "[0.0, 0.0, -90.0, -60.0, 0.0]"
```

Runtime changes are not persisted — copy the final values back into
`servo_bridge.yaml` and rebuild the package.

### Testing from the Unity Editor

The Editor can drive the real arm without building an APK, which makes
inverse-kinematics tuning far quicker. Tick `Allow Mouse Fallback` on
`DrawingCanvas`, press Play, set both hand presets to `Invisible` on the
`Editor Hand Simulator` that appears, raise `Offset` to Y ≈ 1.2 so the board is
in view, then hold the left mouse button and drag.

## 6. Diagnostics

Check one layer at a time; the layer that goes quiet is the broken one.

| Layer | Command | Expected |
|---|---|---|
| App sends | `adb logcat -s Unity \| grep -i servo` | `Sent Servo control message.` |
| Packets arrive | `sudo tcpdump -i any -n udp port 14551` | packets from the phone |
| Decoded | `ros2 topic echo /wicom/servo_control` | five float values |
| Calibrated | `ros2 topic echo /pca9685_servo/command` | named joints, in degrees |
| Servos move | `ros2 topic pub --once ...` (see §4) | the arm moves |

### Common problems

| Symptom | Cause |
|---|---|
| Arm never moves, logs look clean | `zPlane_cm` ≥ `L1+L2+L3`, workspace is zero |
| Packets vanish although the network is fine | a stale `udp_server_node` still holds the port — `pkill -f udp_server_node` |
| Config edited but behaviour unchanged | nodes read the installed copy: `colcon build --packages-select <pkg>` and restart |
| `package not found` on launch | new shell has not sourced `~/ros2_ws/install/setup.bash` |
| One joint never moves | channel map in `servos.yaml` does not match the wiring — rerun `channel_test.py --scan` |
| A small angle sends a joint to the wrong place | the driver reads any `|angle| < 6.3` as radians; keep `min_angle ≥ 10` |
| Arm snaps hard when servo power returns | the PCA9685 keeps its registers on Pi logic power — always kill the PWM before unplugging |
| Camera screens are grey in the Editor | Vuplex WebView is Android-only; use `MjpegStreamViewer` instead |

## 7. Bench tools

All three talk straight to the hardware over I2C, so stop the arm driver first.

| Tool | Purpose |
|---|---|
| `channel_test.py --scan` | wiggle each PCA9685 channel in turn to map servo → channel |
| `arm_jog` | interactive per-joint jogging; computes calibration offsets |
| `servo_spin.py -c <ch>` | run one servo continuously with live speed and on/off control |

## 8. Safety

- Wire the servo supply separately from the Pi; share ground, never draw servo
  current from the Pi's 5 V pin.
- Set `min_angle` / `max_angle` before the first powered run.
- A servo buzzing at an end stop can burn out within a minute — cut power.
- Never force a powered servo by hand; kill the PWM first.
- Keep a hand on the servo power switch during the first run after any change.
