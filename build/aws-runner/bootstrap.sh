#!/bin/bash
set -euxo pipefail
# Bound the initial running session; always destroy and verify via Terraform.
systemd-run --unit=runner-expiry --on-active=6h /sbin/shutdown -h now
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y curl git rsync unzip libicu74 libssl3t64 libfontconfig1 libfreetype6
install -d -o ubuntu -g ubuntu /home/ubuntu/.dotnet
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
sudo -u ubuntu bash /tmp/dotnet-install.sh --version 11.0.100-rc.1.26425.128 --install-dir /home/ubuntu/.dotnet
sudo -u ubuntu bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /home/ubuntu/.dotnet
# CodingRules' reference-surface tests also require the netstandard 2.1 pack.
curl -fsSL https://api.nuget.org/v3-flatcontainer/netstandard.library.ref/2.1.0/netstandard.library.ref.2.1.0.nupkg -o /tmp/netstandard-ref.nupkg
echo '46ea2fcbd10a817685b85af7ce0c397d12944bdc81209e272de1e05efd33c78a  /tmp/netstandard-ref.nupkg' | sha256sum -c -
mkdir -p /home/ubuntu/.dotnet/packs/NETStandard.Library.Ref/2.1.0
unzip -q /tmp/netstandard-ref.nupkg -d /home/ubuntu/.dotnet/packs/NETStandard.Library.Ref/2.1.0
cat > /etc/profile.d/dotnet.sh <<'PROFILE'
export DOTNET_ROOT=/home/ubuntu/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
PROFILE
touch /home/ubuntu/runner-ready
