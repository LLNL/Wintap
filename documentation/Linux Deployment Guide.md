# Deploying Wintap to Linux - Step-by-Step Guide

## Prerequisites
- Windows machine with Wintap source code
- Ubuntu Linux VM (tested with Multipass)
- .NET 8 SDK installed on both machines

## Step 1: Install .NET 8 Runtime on Ubuntu

```bash
# Add Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Update and install
sudo apt update
sudo apt install -y dotnet-sdk-8.0 aspnetcore-runtime-8.0
```

Verify installation:
```bash
dotnet --version
```

## Step 2: Build and Publish Wintap for Linux

From your Windows machine (in the Wintap solution directory):

```bash
dotnet publish -c Debug -r linux-x64 -p:PublishSingleFile=false -f net8.0
```

On Mac using arm:

```bash
dotnet publish Lintap.csproj -c Debug -r linux-arm64 -p:PublishSingleFile=false -f net8.0
```

## Step 3: Create Wintap Directory on Linux

On the Ubuntu VM:

```bash
mkdir -p ~/wintap/publish
```

## Step 4: Transfer Files to Linux

From Windows (adjust paths as needed):

```bash
# Using multipass
"c:\Program Files\Multipass\bin\multipass.exe" transfer -r .\bin\Debug\net8.0\linux-x64\publish enabled-cattle:/home/ubuntu/wintap/
```

## Step 5: Create Required Directories

On Ubuntu:

```bash
sudo mkdir -p /usr/share/Wintap
sudo chown -R ubuntu:ubuntu /usr/share/Wintap
sudo chmod -R 755 /usr/share/Wintap
```

## Step 6: Create systemd Service

```bash
sudo nano /etc/systemd/system/wintap.service
```

Paste this content:

```ini
[Unit]
Description=Wintap Security Monitoring Service
After=network.target

[Service]
Type=simple
WorkingDirectory=/home/ubuntu/wintap/publish
ExecStart=/home/ubuntu/wintap/publish/Wintap
Restart=on-failure
RestartSec=10
KillMode=mixed
KillSignal=SIGTERM
TimeoutStartSec=300
TimeoutStopSec=90

User=ubuntu
Group=ubuntu

Environment="DOTNET_ENVIRONMENT=Production"
Environment="ASPNETCORE_URLS=http://0.0.0.0:8099"

StandardOutput=journal
StandardError=journal
SyslogIdentifier=wintap

[Install]
WantedBy=multi-user.target
```

Save (Ctrl+O, Enter, Ctrl+X)

## Step 7: Enable and Start Service

```bash
sudo systemctl daemon-reload
sudo systemctl enable wintap
sudo systemctl start wintap
sudo systemctl status wintap
```

## Step 8: Verify Deployment

Check service status:
```bash
sudo systemctl status wintap
```

View logs:
```bash
sudo journalctl -u wintap -f
```

Check Wintap log file:
```bash
tail -f /usr/share/Wintap/Logs/Wintap.log
```

Access web workbench from Windows:
```
http://<vm-ip>:8099
```

(Get VM IP: `ip addr show` or from Windows: `multipass info enabled-cattle`)

## Service Management Commands

```bash
# Stop service
sudo systemctl stop wintap

# Start service
sudo systemctl start wintap

# Restart service
sudo systemctl restart wintap

# View logs (follow mode)
sudo journalctl -u wintap -f

# View recent logs
sudo journalctl -u wintap -n 100
```

## Expected Behavior on Linux

✅ **Working:**
- Service starts and runs as daemon
- Web workbench accessible on port 8099
- Plugin architecture loads
- MCP server functionality
- Linux process collector (basic)
- Event pipeline flows to Esper

⚠️ **Limited/Not Supported:**
- Process tree database persistence (Windows-only for now)

## Troubleshooting

**Service won't start:**
```bash
# Check detailed logs
sudo journalctl -u wintap -n 50 --no-pager
```

**Permission errors:**
```bash
# Fix Wintap data directory permissions
sudo chown -R ubuntu:ubuntu /usr/share/Wintap
```

**Can't access web workbench:**
- Verify service is listening: `curl http://localhost:8099`
- Check firewall if accessing from external machine
- Ensure `ASPNETCORE_URLS=http://0.0.0.0:8099` in service file

**After updating code:**
```bash
# Rebuild on Windows, transfer files, then:
sudo systemctl restart wintap
```