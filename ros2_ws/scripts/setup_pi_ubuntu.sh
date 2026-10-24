#!/usr/bin/env bash
# Cai dat mot lan tren Raspberry Pi vua flash Ubuntu Server 22.04 LTS (64-bit).
# Chay TREN PI:   bash setup_pi_ubuntu.sh
set -euo pipefail

echo "==> Kiem tra he dieu hanh"
. /etc/os-release
if [ "${VERSION_ID:-}" != "22.04" ]; then
  echo "!! Canh bao: dang chay $PRETTY_NAME. ROS 2 Humble can Ubuntu 22.04." >&2
  read -rp "Van tiep tuc? [y/N] " a; [ "$a" = "y" ] || exit 1
fi
if [ "$(dpkg --print-architecture)" != "arm64" ]; then
  echo "!! Kien truc $(dpkg --print-architecture) khong phai arm64 — ROS 2 Humble khong co goi." >&2
  exit 1
fi

echo "==> Cap nhat he thong"
sudo apt update && sudo apt upgrade -y
sudo apt install -y software-properties-common curl gnupg lsb-release
sudo add-apt-repository universe -y

echo "==> Them kho apt cua ROS 2"
ROS_APT_SOURCE_VERSION=$(curl -s https://api.github.com/repos/ros-infrastructure/ros-apt-source/releases/latest \
  | grep -F '"tag_name"' | awk -F'"' '{print $4}')
curl -L -o /tmp/ros2-apt-source.deb \
  "https://github.com/ros-infrastructure/ros-apt-source/releases/download/${ROS_APT_SOURCE_VERSION}/ros2-apt-source_${ROS_APT_SOURCE_VERSION}.$(. /etc/os-release && echo "$VERSION_CODENAME")_all.deb"
sudo apt install -y /tmp/ros2-apt-source.deb
sudo apt update

echo "==> Cai ROS 2 Humble (ros-base, khong kem GUI)"
sudo apt install -y ros-humble-ros-base python3-colcon-common-extensions python3-pip python3-dev

echo "==> Bat I2C cho PCA9685"
CONFIG=/boot/firmware/config.txt
[ -f "$CONFIG" ] || CONFIG=/boot/config.txt
if ! grep -q "^dtparam=i2c_arm=on" "$CONFIG"; then
  echo "dtparam=i2c_arm=on" | sudo tee -a "$CONFIG" > /dev/null
  echo "   da them dtparam=i2c_arm=on vao $CONFIG"
fi
sudo apt install -y i2c-tools libgpiod2
sudo usermod -aG i2c,dialout,gpio "$USER"

echo "==> Cai thu vien dieu khien servo"
pip3 install --upgrade adafruit-circuitpython-servokit adafruit-extended-bus

echo "==> Them source ROS vao ~/.bashrc"
grep -q "source /opt/ros/humble/setup.bash" ~/.bashrc || \
  echo "source /opt/ros/humble/setup.bash" >> ~/.bashrc
grep -q "source ~/ros2_ws/install/setup.bash" ~/.bashrc || \
  echo "[ -f ~/ros2_ws/install/setup.bash ] && source ~/ros2_ws/install/setup.bash" >> ~/.bashrc

echo
echo "───────────────────────────────────────────────"
echo " Xong. KHOI DONG LAI de I2C va nhom user co hieu luc:"
echo "     sudo reboot"
echo " Sau khi boot lai, kiem tra:"
echo "     i2cdetect -y 1        # phai thay 40"
echo "     ros2 --help"
echo "───────────────────────────────────────────────"
