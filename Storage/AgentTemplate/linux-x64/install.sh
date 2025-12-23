#!/bin/bash
# WatchSec Agent Installer (Linux/Mac)
# Run as Root (sudo)

set -e

if [ "$EUID" -ne 0 ]; then
  echo "Please run as root (sudo ./install.sh)"
  exit 1
fi

echo "[*] Starting WatchSec Agent Installation..."

INSTALL_DIR="/opt/watch-sec-agent"
SOURCE_DIR="$(dirname "$0")"

# 1. Stop Service if running
if systemctl is-active --quiet watch-sec-agent; then
    echo "[-] Stopping existing service..."
    systemctl stop watch-sec-agent
fi

# 2. Copy Files
echo "[*] Installing to $INSTALL_DIR..."
mkdir -p "$INSTALL_DIR"
cp -r "$SOURCE_DIR"/* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/watch-sec-agent"

# 3. Create Systemd Service (Linux)
if [ -d "/etc/systemd/system" ]; then
    echo "[*] Creating Systemd Service..."
    cat > /etc/systemd/system/watch-sec-agent.service <<EOF
[Unit]
Description=WatchSec Enterprise Agent
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/watch-sec-agent
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable watch-sec-agent
    systemctl start watch-sec-agent
    echo "[+] Service started (Systemd)."

# 4. Create Launchd Agent (macOS)
elif [ -d "/Library/LaunchDaemons" ]; then
    echo "[*] Creating Launchd Service (macOS)..."
    cat > /Library/LaunchDaemons/com.watchsec.agent.plist <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.watchsec.agent</string>
    <key>ProgramArguments</key>
    <array>
        <string>$INSTALL_DIR/watch-sec-agent</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>WorkingDirectory</key>
    <string>$INSTALL_DIR</string>
</dict>
</plist>
EOF
    
    launchctl load -w /Library/LaunchDaemons/com.watchsec.agent.plist
    echo "[+] Service started (Launchd)."
else
    echo "[!] Could not detect Systemd or Launchd. You may need to run the agent manually."
    exit 1
fi

echo "[+] Installation Complete!"
