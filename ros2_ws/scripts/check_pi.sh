#!/usr/bin/env bash
# Kiem tra mot the SD / Raspberry Pi co san sang chay wicom arm stack khong.
# Chay TREN PI:  bash check_pi.sh
# Khong sua doi gi ca, chi doc va bao cao.

PASS=0; WARN=0; FAIL=0
ok()   { echo "  [ OK ]  $1"; PASS=$((PASS+1)); }
warn() { echo "  [WARN]  $1"; WARN=$((WARN+1)); }
bad()  { echo "  [FAIL]  $1"; FAIL=$((FAIL+1)); }

echo "══════════════════════════════════════════════════════"
echo " KIEM TRA MOI TRUONG - Virtual-Copilot-UAM arm stack"
echo "══════════════════════════════════════════════════════"

echo
echo "── 1. He dieu hanh & phan cung ──────────────────────"
. /etc/os-release 2>/dev/null
echo "  OS      : ${PRETTY_NAME:-khong doc duoc}"
echo "  Arch    : $(dpkg --print-architecture 2>/dev/null)"
echo "  Model   : $(cat /proc/device-tree/model 2>/dev/null | tr -d '\0' || true)$([ -e /proc/device-tree/model ] || echo 'khong phai Raspberry Pi')"
echo "  Kernel  : $(uname -r)"
echo "  RAM     : $(free -m | awk '/^Mem:/{print $2" MB"}')"
echo "  Trong   : $(df -h / | awk 'NR==2{print $4" free / "$2}')"

[ "${VERSION_ID:-}" = "22.04" ] && ok "Ubuntu 22.04 (dung ban cho ROS 2 Humble)" \
  || warn "Khong phai Ubuntu 22.04 — xem bang kha nang ben duoi"
[ "$(dpkg --print-architecture)" = "arm64" ] && ok "Kien truc arm64" \
  || bad "Khong phai arm64 — ROS 2 khong co goi apt cho kien truc nay"

AVAIL_KB=$(df --output=avail / | tail -1)
[ "$AVAIL_KB" -gt 2000000 ] && ok "Con du dung luong (>2GB)" \
  || warn "Con it dung luong, colcon build co the that bai"

MEM=$(free -m | awk '/^Mem:/{print $2}')
[ "$MEM" -ge 1800 ] || warn "RAM ${MEM}MB — build nen them swap hoac dung: colcon build --executor sequential"

echo
echo "── 2. ROS 2 ─────────────────────────────────────────"
DISTROS=$(ls /opt/ros/ 2>/dev/null)
if [ -z "$DISTROS" ]; then
  bad "Khong tim thay ROS nao trong /opt/ros"
else
  echo "  Tim thay: $DISTROS"
  for d in $DISTROS; do
    case "$d" in
      humble) ok "ROS 2 Humble — khop hoan toan voi project" ;;
      iron|jazzy|rolling|foxy|galactic)
        warn "ROS 2 $d — khac ban khuyen nghi, nhung code chi dung rclpy/std_msgs nen nhieu kha nang van chay" ;;
      noetic|melodic) bad "ROS 1 $d — KHONG chay duoc package ROS 2" ;;
      *) warn "Ban la: $d" ;;
    esac
  done
  ROSD=$(echo "$DISTROS" | grep -E 'humble|iron|jazzy|rolling' | head -1)
  if [ -n "$ROSD" ]; then
    # shellcheck disable=SC1090
    source "/opt/ros/$ROSD/setup.bash" 2>/dev/null
    command -v ros2 >/dev/null && ok "Lenh 'ros2' chay duoc ($ROSD)" || bad "Khong chay duoc lenh ros2"
    python3 -c "import rclpy" 2>/dev/null && ok "Python import rclpy OK" || bad "Khong import duoc rclpy"
    python3 -c "from rcl_interfaces.msg import SetParametersResult" 2>/dev/null \
      && ok "rcl_interfaces OK" || bad "Thieu rcl_interfaces"
  fi
fi
command -v colcon >/dev/null && ok "colcon da cai" \
  || bad "Thieu colcon: sudo apt install python3-colcon-common-extensions"

echo
echo "── 3. I2C / PCA9685 ─────────────────────────────────"
CONFIG=/boot/firmware/config.txt; [ -f "$CONFIG" ] || CONFIG=/boot/config.txt
if [ -f "$CONFIG" ]; then
  grep -q "^dtparam=i2c_arm=on" "$CONFIG" && ok "dtparam=i2c_arm=on co trong $CONFIG" \
    || bad "Chua bat I2C trong $CONFIG (them dong: dtparam=i2c_arm=on roi reboot)"
else
  warn "Khong tim thay config.txt (may nay khong phai Pi?)"
fi

[ -e /dev/i2c-1 ] && ok "/dev/i2c-1 ton tai" || bad "Khong co /dev/i2c-1 — I2C chua bat hoac chua reboot"
command -v i2cdetect >/dev/null && ok "i2c-tools da cai" || warn "Thieu i2c-tools: sudo apt install i2c-tools"

if command -v i2cdetect >/dev/null && [ -e /dev/i2c-1 ]; then
  if i2cdetect -y 1 2>/dev/null | grep -qE '\b40\b'; then
    ok "Thay PCA9685 tai dia chi 0x40"
  else
    warn "Khong thay 0x40 tren bus 1 (chua cam board? chua cap nguon logic?)"
  fi
fi

id -nG | grep -qw i2c && ok "User thuoc nhom i2c" || bad "User chua vao nhom i2c: sudo usermod -aG i2c \$USER && reboot"

python3 -c "import adafruit_servokit" 2>/dev/null && ok "adafruit_servokit import duoc" \
  || bad "Thieu thu vien: pip3 install adafruit-circuitpython-servokit"
python3 -c "import adafruit_extended_bus" 2>/dev/null && ok "adafruit_extended_bus co (chon duoc bus I2C)" \
  || warn "Thieu adafruit-extended-bus (khong bat buoc, chi can khi dung bus khac bus mac dinh)"

echo
echo "── 4. Mang ──────────────────────────────────────────"
ip -brief addr | grep -v '^lo' | sed 's/^/  /'
systemctl is-active --quiet hostapd 2>/dev/null && warn "hostapd DANG CHAY — may nay dang phat Wi-Fi" \
  || echo "  hostapd: khong chay"
systemctl is-active --quiet NetworkManager 2>/dev/null && echo "  NetworkManager: dang chay (tao hotspot bang nmcli duoc)" \
  || echo "  NetworkManager: khong chay (dang dung netplan/systemd-networkd)"
ss -lunp 2>/dev/null | grep -q ':14551' && warn "Cong UDP 14551 dang bi chiem" || ok "Cong UDP 14551 dang ranh"

echo
echo "── 5. Workspace ─────────────────────────────────────"
if [ -d ~/ros2_ws/src ]; then
  echo "  ~/ros2_ws/src dang co: $(ls ~/ros2_ws/src 2>/dev/null | tr '\n' ' ')"
  [ -d ~/ros2_ws/install ] && ok "Da tung build (co thu muc install)" || warn "Chua build lan nao"
else
  echo "  Chua co ~/ros2_ws — deploy_to_pi.sh se tu tao"
fi

echo
echo "══════════════════════════════════════════════════════"
echo "  KET QUA:  $PASS OK   |   $WARN canh bao   |   $FAIL loi"
if [ "$FAIL" -eq 0 ] && [ "$WARN" -eq 0 ]; then
  echo "  → San sang. Chay deploy_to_pi.sh tu may dev."
elif [ "$FAIL" -eq 0 ]; then
  echo "  → Dung duoc. Doc lai cac dong [WARN] o tren."
else
  echo "  → Con $FAIL loi phai sua truoc khi deploy."
fi
echo "══════════════════════════════════════════════════════"
