#!/usr/bin/env bash
# Day 2 package cua project sang Pi roi build.
# Chay TREN MAY DEV (WSL):   bash scripts/deploy_to_pi.sh [user@host]
#
# AN TOAN: chi dong toi 2 thu muc wicom_roboarm_ros2 va wicom_udp_ros2.
# Moi package khac trong ~/ros2_ws/src cua Pi (px4_msgs, camera_ros,
# ldlidar_ros2, wicom_roboarm ...) deu KHONG bi dung toi.
set -euo pipefail

PI="${1:-${PI_HOST:-piros2@192.168.50.1}}"
PKGS=(wicom_roboarm_ros2 wicom_udp_ros2)
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/src"
STAMP=$(date +%Y%m%d-%H%M%S)

echo "==> Sao luu ban dang co tren Pi truoc khi ghi de"
ssh "$PI" "bash -lc '
  cd ~/ros2_ws/src 2>/dev/null || exit 0
  EXIST=\$(ls -d ${PKGS[*]} 2>/dev/null || true)
  if [ -n \"\$EXIST\" ]; then
    tar czf ~/wicom_backup_${STAMP}.tar.gz \$EXIST
    echo \"    da luu: ~/wicom_backup_${STAMP}.tar.gz (\$EXIST)\"
  else
    echo \"    chua co package nao, bo qua\"
  fi
'"

for pkg in "${PKGS[@]}"; do
  echo "==> Day $pkg"
  rsync -a --delete \
    --exclude build --exclude install --exclude log --exclude '__pycache__' \
    "$SRC/$pkg/" "$PI:ros2_ws/src/$pkg/"
done

echo "==> Don build/install cu cua 2 package (tranh loi symlink con sot)"
# Danh sach duoc noi O MAY DEV roi moi gui di — khong co bien nao no ra rong
# tren Pi, nen khong co rui ro xoa nham ca build/ va install/.
CLEAN=""
for pkg in "${PKGS[@]}"; do CLEAN="$CLEAN build/$pkg install/$pkg"; done
echo "    se xoa:$CLEAN"
ssh "$PI" "cd ~/ros2_ws && rm -rf$CLEAN && echo '    da don'"

echo "==> Build (chi 2 package nay, khong dung toi px4_msgs)"
ssh "$PI" "bash -lc '
  source /opt/ros/humble/setup.bash &&
  cd ~/ros2_ws &&
  colcon build --packages-select ${PKGS[*]} &&
  echo BUILD-OK
'"

echo
echo "Xong. Chay tren Pi:"
echo "    source ~/ros2_ws/install/setup.bash"
echo "    ros2 launch wicom_roboarm_ros2 wicom_arm_full.launch.py"
