# Initial testing of building on Ubuntu in Multipass

* Removed "reg" commands from Wintap.csproj

## Build
```bash
cd wintap
dotnet build -p:LinkRuntime=true -p:RuntimeLinkerOptions="-rpath '\$ORIGIN'"
```

or, for cross platform:

```bash
dotnet publish -c Release -r linux-arm64 --self-contained -p:PublishSingleFile=true
```

## Run

```bash
# Writes logs, as ubuntu, to:
sudo mkdir /usr/share/Wintap
sudo chown ubuntu /usr/share/Wintap
cd bin/Release/net8.0
./Wintap
```
