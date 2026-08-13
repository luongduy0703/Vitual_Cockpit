# Virtual Copilot for UAM

This project provides a VR-based control interface for Unmanned Aerial Vehicles (UAVs) equipped with robotic arms, utilizing Unity for the VR environment and ROS for drone communication.

## 1. System Requirements

### Development Environment (PC/Laptop)

- **Operating System:** Windows 10 or Windows 11.
- **Software:** 
  - [Unity Hub](https://unity.com/download)
  - Visual Studio

### UAV Configuration

- **Middleware:** ROS 2 (Humble Hawksbill).
- **OS:** Ubuntu 22.04 Server (running on Raspberry Pi 4).

## 2. Installation & Setup

### Clone the Repository

Open terminal and run:

```
git clone 
```

### Unity Project Configuration

1. Launch **Unity Hub** and click **Open** to select the `Virtual-Copilot-UAM` folder.
2. Unity Hub will automatically prompt you to install the required Unity Editor version. Please allow it to finish.
3. Once the project is open, go to **File > Build Settings**.
4. Select **Android** from the platform list and click **Switch Platform**.
5. Verify ROS2 mode: go to **Robotics > ROS Settings** and confirm **Protocol** is set to **ROS2**.

### Raspberry Pi Setup (ROS2 Humble)

1. Flash **Ubuntu 22.04 Server ARM64** onto the Raspberry Pi's SD card.
2. Install ROS 2 Humble:

```bash
sudo apt update && sudo apt install -y locales software-properties-common curl
sudo locale-gen en_US en_US.UTF-8
sudo update-locale LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8

sudo curl -sSL https://raw.githubusercontent.com/ros/rosdistro/master/ros.key \
  -o /usr/share/keyrings/ros-archive-keyring.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/ros-archive-keyring.gpg] \
  http://packages.ros.org/ros2/ubuntu $(. /etc/os-release && echo $UBUNTU_CODENAME) main" \
  | sudo tee /etc/apt/sources.list.d/ros2.list > /dev/null

sudo apt update
sudo apt install -y ros-humble-ros-base python3-colcon-common-extensions
echo "source /opt/ros/humble/setup.bash" >> ~/.bashrc
```

3. Build the ROS2 workspace:

```bash
# Navigate to the workspace included in the cloned project:
cd ~/Virtual-Copilot-UAM/ros2_ws
colcon build
source install/setup.bash
```

## 3. Deployment (Build to Android)

1. Enable **Developer Mode** and **USB Debugging** on your Android smartphone.
2. Connect the smartphone to your PC/Laptop via a USB cable.
3. In Unity, navigate to **File > Build and Run**.
4. Wait for the APK to build and install automatically on your device.

## 4. Testing & Execution

### Connection Setup

1. Connect your Android phone to the drone's Wi-Fi access point:
   - **SSID:** `Clover-xxxx`
   - **Password:** `cloverwifi`
2. Launch the **VR app** on your phone.
   - **Status Check:** If the video stream is not visible, verify the IP address settings in the **Communication Setup** section of the app.

### Drone-side Configuration (SSH)

Access the drone's onboard computer (Raspberry Pi) via SSH:

- **Command:** `ssh pi@192.168.11.1`
- **Password:** `1`

Run the following commands in separate terminals:

**Terminal 1: Start UDP Server**

```bash
ros2 launch wicom_udp_ros2 wicom_udp.launch.py
```

*Or run directly with parameters:*

```bash
ros2 run wicom_udp_ros2 udp_server_node --ros-args \
  -p alt_hold.enable:=true \
  -p alt_hold.target_z:=0.8 \
  -p alt_hold.kp:=1.0 \
  -p alt_hold.kd:=0.6 \
  -p alt_hold.max_vz:=0.5 \
  -p vx_override.enable:=false
```

**Terminal 2: Start Robotic Arm Control**

```bash
ros2 launch wicom_roboarm_ros2 wicom_roboarm.launch.py
```

### Quick Local Testing (PC / Linux Simulation)

To test the system locally without a physical drone or phone:

1. **Start ROS 2 Node locally:**
   ```bash
   cd ~/Virtual-Copilot-UAM/ros2_ws
   colcon build && source install/setup.bash
   ros2 launch wicom_udp_ros2 wicom_udp.launch.py
   ```
2. **Run Unity Play Mode:**
   - Open Unity Editor, select `Communication` GameObject.
   - Set Hostname = `127.0.0.1`, Port = `14550`.
   - Press **Play**, right-click `Communication (Script)` in Inspector and select **`TEST: Start UDP Connection`**, then **`TEST: Send ARM Command`**.
3. **Verify Topics via ROS 2 CLI:**
   ```bash
   ros2 topic echo /wicom/cmd/arm
   ```

## 5. VR App Interaction Guide

| **Action**               | **Execution**                             | **Expected Result**                              |
| ------------------------ | ----------------------------------------- | ------------------------------------------------ |
| **Establish Connection** | Tap the "Connect" button in 3D space      | Signal returns `True` (Green indicator)          |
| **Voice Command**        | Say **"Kết nối"**                         | Signal returns `True`                            |
| **Enable Drawing**       | Tap "Show Draw" in 3D space               | The Draw Area appears                            |
| **Control Arm**          | Draw in the air with your hand/controller | The Robotic Arm mimics the movement in real-time |

