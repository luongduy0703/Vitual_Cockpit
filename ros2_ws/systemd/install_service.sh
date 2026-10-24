#!/usr/bin/env bash
# Cai 2 service tu khoi dong. Chay TREN PI:  bash systemd/install_service.sh
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sudo cp "$DIR/wicom-arm-driver.service" /etc/systemd/system/wicom-arm-driver@.service
sudo cp "$DIR/wicom-arm.service"        /etc/systemd/system/wicom-arm@.service
sudo systemctl daemon-reload
sudo systemctl enable --now "wicom-arm-driver@${USER}.service"
sleep 3
sudo systemctl enable --now "wicom-arm@${USER}.service"

echo
echo "Da bat 2 service (driver chay truoc, cau UDP chay sau):"
echo "    systemctl status wicom-arm-driver@${USER} wicom-arm@${USER}"
echo "    journalctl -u wicom-arm-driver@${USER} -u wicom-arm@${USER} -f"
echo "    sudo systemctl stop wicom-arm@${USER} wicom-arm-driver@${USER}"
