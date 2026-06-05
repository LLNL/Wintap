# Deploying Lintap to Linux - Step-by-Step Guide

## Prerequisites
- Development machine with Lintap source code
- Ubuntu Linux system (24.04+ recommended, tested with Multipass)
- .NET 8 SDK installed on development machine
- Root/sudo access on target Linux system

## Step 1: Install Dependencies on Ubuntu

### .NET 8 Runtime
```bash
# Add Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Update and install
sudo apt update
sudo apt install -y dotnet-sdk-8.0 aspnetcore-runtime-8.0
```

### eBPF Development Tools
```bash
# Install eBPF toolchain and dependencies
sudo apt install -y \
    libbpf-dev \
    libbpf1 \
    linux-headers-$(uname -r) \
    clang \
    llvm \
    libelf-dev \
    linux-tools-common \
    build-essential

# Verify installations
clang --version
bpftool version
dotnet --version
```

_On OSX ARM, you will get an error about a missing include (asm/types.h). Fix with:_

```bash
# The headers should already be there, just create the symlink
sudo ln -s /usr/include/aarch64-linux-gnu/asm /usr/include/asm
```

### Verify eBPF Support
```bash
# Check if BPF is enabled in kernel
cat /boot/config-$(uname -r) | grep BPF

# Should see CONFIG_BPF=y and CONFIG_BPF_SYSCALL=y
```

## Step 2: Build eBPF Programs

On the Linux development system (or target if building locally):
```bash
cd ~/gitlab/Lintap/wintap/platform/linux/sensor/ebpf/tracers

# Build all eBPF tracer programs
make all

# Or build individually:
# make clone
# make execve
# make exit
# make network
# make fileops

# Verify .bpf.o files were created
ls -lh *.bpf.o
```

## Step 3: Build and Publish Lintap for Linux

From your development machine (in the Lintap solution directory):
```bash
# Build
dotnet publish \
    -c Debug \
    -p:PublishSingleFile=false \
    -f net8.0 
    Lintap.csproj

# Output will be in: wintap/bin/Release/net8.0/linux-x64/publish/
```

## Step 4: Create Lintap Directory on Linux

On the Ubuntu system:
```bash
# Create application directory
sudo mkdir -p /opt/lintap
sudo chown -R $USER:$USER /opt/lintap

# Create data directory
sudo mkdir -p /var/lib/lintap
sudo chown -R $USER:$USER /var/lib/lintap

# Create log directory
sudo mkdir -p /var/log/lintap
sudo chown -R $USER:$USER /var/log/lintap
```

## Step 5: Transfer Files to Linux

### Option A: Using multipass (from Windows/Mac)
```bash
multipass transfer -r ./wintap/bin/Debug/net8.0/publish/* <vm-name>:/opt/lintap/
```

### Option B: Using scp
```bash
scp -r ./wintap/bin/Debug/net8.0/publish/* user@linux-host:/opt/lintap/
```

### Option C: Local build
```bash
# If building on the target Linux system
cp -r ./wintap/bin/Debug/net8.0/publish/* /opt/lintap/
```

## Step 6: Verify eBPF Tracers

Ensure eBPF object files are in the correct location:
```bash
# Check that tracers directory exists
ls -lh /opt/lintap/tracers/

# Should contain:
# - clone_tracer.bpf.o
# - execve_tracer.bpf.o
# - exit_tracer.bpf.o
# - network_ops_tracer.bpf.o
# - file_ops_tracer.bpf.o
```

## Step 7: Create systemd Service
```bash
sudo nano /etc/systemd/system/lintap.service
```

Paste this content:
```ini
[Unit]
Description=Lintap Security Monitoring Service
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/lintap
ExecStart=/opt/lintap/Lintap
Restart=on-failure
RestartSec=10
KillMode=mixed
KillSignal=SIGTERM
TimeoutStartSec=300
TimeoutStopSec=90

# IMPORTANT: Lintap requires root for eBPF operations
User=root
Group=root

Environment="DOTNET_ENVIRONMENT=Production"
Environment="ASPNETCORE_URLS=http://0.0.0.0:8099"

StandardOutput=journal
StandardError=journal
SyslogIdentifier=lintap

[Install]
WantedBy=multi-user.target
```

**Note:** Lintap requires root privileges to load eBPF programs. The service runs as root but includes security hardening directives.

Save (Ctrl+O, Enter, Ctrl+X)

## Step 8: Enable and Start Service
```bash
sudo systemctl daemon-reload
sudo systemctl enable lintap
sudo systemctl start lintap
sudo systemctl status lintap
```

## Step 9: Verify Deployment

Check service status:
```bash
sudo systemctl status lintap
```

View logs:
```bash
sudo journalctl -u lintap -f
```

Check Lintap log file:
```bash
tail -f /var/log/lintap/Lintap.log
```

Verify eBPF programs are loaded:
```bash
sudo bpftool prog list | grep -i lintap
```

Access web workbench:
```
http://<linux-host-ip>:8099
```

(Get IP: `ip addr show` or `hostname -I`)

## Step 10: Test eBPF Sensors

Generate some activity to test sensors:
```bash
# Test process creation (execve sensor)
ls -la

# Test process forking (clone sensor)
bash -c "echo test"

# Test network activity (network sensor)
curl https://example.com

# Test file operations (fileops sensor)
touch /tmp/test.txt && rm /tmp/test.txt
```

Run the network capture smoke test to verify that generated HTTP/HTTPS traffic appears in collected parquet data:

```bash
# Example using a temporary data root for a foreground/manual test run
sudo rm -rf /tmp/lintap-smoke
mkdir -p /tmp/lintap-smoke
cd /opt/lintap
sudo env WINTAP_DATA_ROOT=/tmp/lintap-smoke ./Lintap

# In another shell on the same host/VM
python3 /path/to/wintap/devtools/network_capture_smoke_test.py \
    --data-root /tmp/lintap-smoke \
    --timeout 240 \
    --poll-interval 10
```

A passing run should show recent outbound TCP records for ports 80/443 and the local host/VM IP address with ephemeral local ports, for example:

```text
local=192.168.252.9:37804 -> remote=104.20.23.154:80 proto=TCP rows=2
PASS: captured recent outbound network records for the generated traffic.
```

## Service Management Commands
```bash
# Stop service
sudo systemctl stop lintap

# Start service
sudo systemctl start lintap

# Restart service
sudo systemctl restart lintap

# View logs (follow mode)
sudo journalctl -u lintap -f

# View recent logs
sudo journalctl -u lintap -n 100

# View eBPF-specific logs
sudo journalctl -u lintap | grep -i ebpf
```

## Expected Behavior on Linux

✅ **Working:**
- Service starts and runs as daemon
- Web workbench accessible on port 8099
- Plugin architecture loads
- MCP server functionality
- eBPF-based process monitoring (execve, clone, exit)
- eBPF-based network monitoring
- eBPF-based file operations monitoring
- Event pipeline flows to Esper
- Real-time system telemetry collection

⚠️ **Known Limitations:**
- Requires root privileges for eBPF operations
- Some Windows-specific features not available (ETW, COM+, etc.)

## Troubleshooting

### Service won't start
```bash
# Check detailed logs
sudo journalctl -u lintap -n 50 --no-pager

# Check if process is running
ps aux | grep Lintap
```

### eBPF programs fail to load
```bash
# Check kernel BPF support
cat /proc/sys/kernel/unprivileged_bpf_disabled

# Verify libbpf is installed
ldconfig -p | grep libbpf

# Check BPF filesystem is mounted
mount | grep bpf

# Manually test loading a BPF program
sudo bpftool prog load /opt/lintap/tracers/clone_tracer.bpf.o /sys/fs/bpf/test_clone
```

### Permission errors
```bash
# Ensure service runs as root (required for eBPF)
sudo systemctl cat lintap | grep User

# Fix data directory permissions if needed
sudo chown -R root:root /opt/lintap
sudo chmod -R 755 /opt/lintap
sudo chown -R root:root /var/lib/lintap
sudo chmod -R 755 /var/lib/lintap
```

### Can't access web workbench
- Verify service is listening: `curl http://localhost:8099`
- Check firewall: `sudo ufw status`
- If using firewall, allow port: `sudo ufw allow 8099/tcp`
- Ensure `ASPNETCORE_URLS=http://0.0.0.0:8099` in service file

### eBPF tracers not found
```bash
# Verify tracers exist
ls -lh /opt/lintap/tracers/

# If missing, rebuild and copy
cd ~/gitlab/Lintap/wintap/platform/linux/sensor/ebpf/tracers
make clean && make all
sudo cp *.bpf.o /opt/lintap/tracers/
sudo systemctl restart lintap
```

### After updating code
```bash
# Rebuild eBPF programs
cd wintap/platform/linux/sensor/ebpf/tracers
make clean && make all

# Rebuild Lintap
dotnet publish -c Release -r linux-x64

# Stop service
sudo systemctl stop lintap

# Update files
sudo cp -r bin/Release/net8.0/linux-x64/publish/* /opt/lintap/

# Restart service
sudo systemctl start lintap
```

## Development Notes

### Rebuilding eBPF Programs Only
```bash
cd wintap/platform/linux/sensor/ebpf/tracers
make clean
make all
sudo cp *.bpf.o /opt/lintap/tracers/
sudo systemctl restart lintap
```

### Viewing eBPF Debug Info
```bash
# List loaded BPF programs
sudo bpftool prog list

# Show program details
sudo bpftool prog show id <id>

# Dump program bytecode
sudo bpftool prog dump xlated id <id>
```

## Security Considerations

- Lintap requires root privileges to load eBPF programs into the kernel
- The systemd service includes security hardening directives where possible
- eBPF programs are verified by the kernel before loading
- Consider running behind a firewall and restricting web workbench access
- Review logs regularly for any suspicious activity

## Minimum Kernel Requirements

- Linux Kernel: 5.8+ (for CO-RE eBPF support)
- Ubuntu: 20.04+ (24.04+ recommended)
- BTF (BPF Type Format) enabled in kernel

Check your kernel version:
```bash
uname -r
```