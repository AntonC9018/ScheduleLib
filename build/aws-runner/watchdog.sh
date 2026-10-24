#!/bin/bash
# Run as root. A connection renews the lease; background jobs do not.
set -euo pipefail
install -o ubuntu -g ubuntu -m 0644 /dev/null /run/runner-heartbeat
cat > /usr/local/sbin/runner-idle-check <<'CHECK'
#!/bin/bash
set -euo pipefail
heartbeat=/run/runner-heartbeat
if [ ! -e "$heartbeat" ]; then
    install -o ubuntu -g ubuntu -m 0644 /dev/null "$heartbeat"
fi
if (( $(date +%s) - $(stat -c %Y "$heartbeat") >= 600 )); then
    logger -t runner-watchdog 'No SSH heartbeat for 10 minutes; powering off'
    /sbin/shutdown -h now
fi
CHECK
chmod 0755 /usr/local/sbin/runner-idle-check
# sshd runs this for noninteractive commands too. Preserve any existing hook.
touch /etc/ssh/sshrc
grep -qxF 'touch /run/runner-heartbeat' /etc/ssh/sshrc || echo 'touch /run/runner-heartbeat' >> /etc/ssh/sshrc
cat > /etc/systemd/system/runner-idle-check.service <<'SERVICE'
[Unit]
Description=Power off runner after ten minutes without an SSH heartbeat
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/runner-idle-check
SERVICE
cat > /etc/systemd/system/runner-idle-check.timer <<'TIMER'
[Unit]
Description=Check the runner SSH heartbeat every fifteen seconds
[Timer]
OnBootSec=30s
OnUnitActiveSec=15s
AccuracySec=1s
[Install]
WantedBy=timers.target
TIMER
systemctl daemon-reload
systemctl enable --now runner-idle-check.timer
