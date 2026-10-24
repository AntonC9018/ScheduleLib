#!/bin/bash
set -euxo pipefail
# The timer survives shell disconnects; shutdown terminates the instance and disk.
systemd-run --unit=runner-expiry --on-active=6h /sbin/shutdown -h now
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y curl git rsync unzip libicu74 libssl3t64 libfontconfig1 libfreetype6
install -d -o ubuntu -g ubuntu /home/ubuntu/.dotnet
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
sudo -u ubuntu bash /tmp/dotnet-install.sh --version 11.0.100-rc.1.26425.128 --install-dir /home/ubuntu/.dotnet
sudo -u ubuntu bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /home/ubuntu/.dotnet
cat > /etc/profile.d/dotnet.sh <<'PROFILE'
export DOTNET_ROOT=/home/ubuntu/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
PROFILE
touch /home/ubuntu/runner-ready
