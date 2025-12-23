#!/bin/bash
set -e

# WatchSec Enterprise Installer (Linux/macOS)
# This script is generated dynamically. Do not edit.

TENANT_KEY="{{TENANT_KEY}}"
INSTALL_DIR="/opt/watch-sec-agent"
CONFIG_DIR="/etc/watch-sec"
TEMP_DIR=$(mktemp -d)

echo "[*] WatchSec Agent Installer"

# 1. Check Root
if [ "$EUID" -ne 0 ]; then
  echo "[!] Please run as root (sudo)."
  exit 1
fi

# 2. Extract Payload
echo "[*] Extracting payload..."
# Find the line number where the payload starts
PAYLOAD_LINE=$(awk '/^__PAYLOAD_BEGINS__/ {print NR + 1; exit 0; }' "$0")

if [ -z "$PAYLOAD_LINE" ]; then
  echo "[!] Installer corrupted: Payload not found."
  exit 1
fi

# Extract zip from this script to temp file
tail -n +$PAYLOAD_LINE "$0" > "$TEMP_DIR/payload.zip"

# Unzip
unzip -q "$TEMP_DIR/payload.zip" -d "$TEMP_DIR/extracted"

# 3. Secure Configuration
echo "[*] Configuring Agent..."
mkdir -p "$CONFIG_DIR"
echo "TenantApiKey=$TENANT_KEY" > "$CONFIG_DIR/agent.conf"
chmod 600 "$CONFIG_DIR/agent.conf" # Only root can read
echo "[+] API Key stored securely in $CONFIG_DIR/agent.conf"

# 4. Install Files
echo "[*] Installing to $INSTALL_DIR..."
# Stop existing service if running
if systemctl is-active --quiet watch-sec-agent; then
    systemctl stop watch-sec-agent
fi

rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
cp -r "$TEMP_DIR/extracted/"* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/watch-sec-agent"

# 5. Register Service
OS="$(uname -s)"
if [ "$OS" = "Linux" ]; then
    if [ -d "/etc/systemd/system" ]; then
        echo "[*] Registering Systemd Service (Linux)..."
        cat <<EOF > /etc/systemd/system/watch-sec-agent.service
[Unit]
Description=WatchSec Enterprise Agent
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/watch-sec-agent
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

        systemctl daemon-reload
        systemctl enable watch-sec-agent
        systemctl start watch-sec-agent
        echo "[+] Service started."
    else
        echo "[!] Systemd not found. You may need to run the agent manually."
    fi

elif [ "$OS" = "Darwin" ]; then
    echo "[*] Registering Launchd Service (macOS)..."
    PLIST="/Library/LaunchDaemons/com.watchsec.agent.plist"
    
    cat <<EOF > "$PLIST"
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
    <key>StandardOutPath</key>
    <string>/var/log/watch-sec.log</string>
    <key>StandardErrorPath</key>
    <string>/var/log/watch-sec.err</string>
    <key>WorkingDirectory</key>
    <string>$INSTALL_DIR</string>
</dict>
</plist>
EOF
    
    chmod 644 "$PLIST"
    launchctl unload "$PLIST" 2>/dev/null || true
    launchctl load -w "$PLIST"
    echo "[+] Service started via launchd."
else
    echo "[!] Unknown OS: $OS. Please run agent manually."
fi

# 6. Cleanup
rm -rf "$TEMP_DIR"
echo "[+] Installation Complete!"
exit 0

__PAYLOAD_BEGINS__
