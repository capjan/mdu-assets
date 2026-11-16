---
navigation.order: 30
description: Run the MDU Asset Server as a native program on Linux or macOS, without Docker, behind your own reverse proxy.
---

# Native program

Every [release](https://github.com/capjan/mdu-assets/releases) has archives for Linux x64, Linux arm64 and macOS on Apple silicon. Each holds a folder with the program `mdu-assets`, the native libraries it needs (libvips and SQLite), the license and the third-party notices. The program needs no .NET runtime. The macOS program and its libraries are signed and notarized.

| Archive | System |
| --- | --- |
| `mdu-assets-<version>-linux-x64.tar.gz` | Linux x64 with glibc 2.35 or newer (Ubuntu 22.04, Debian 12) |
| `mdu-assets-<version>-linux-arm64.tar.gz` | Linux arm64 with glibc 2.35 or newer |
| `mdu-assets-<version>-osx-arm64.tar.gz` | macOS on Apple silicon |

`SHA256SUMS` in the release lists the checksums.

## Install

```bash title="Linux x64, as root"
version=1.0.0
cd /opt
curl -fsSLO "https://github.com/capjan/mdu-assets/releases/download/v$version/mdu-assets-$version-linux-x64.tar.gz"
tar -xzf "mdu-assets-$version-linux-x64.tar.gz"
ln -sfn "mdu-assets-$version-linux-x64" mdu-assets
useradd --system --home-dir /var/lib/mdu-assets --create-home mdu-assets
```

Keep the program in its folder: it loads the libraries next to it. The symbolic link `/opt/mdu-assets` makes updates a matter of extracting the next version and moving the link.

## Run as a service

The server listens on the address in `ASPNETCORE_URLS`. Bind it to loopback, so only the reverse proxy on the same machine reaches it; loopback is always a trusted proxy.

```ini title="/etc/systemd/system/mdu-assets.service"
[Unit]
Description=MDU Asset Server
After=network.target

[Service]
User=mdu-assets
WorkingDirectory=/var/lib/mdu-assets
ExecStart=/opt/mdu-assets/mdu-assets
EnvironmentFile=/etc/mdu-assets.env
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

```bash title="/etc/mdu-assets.env"
ASPNETCORE_URLS=http://127.0.0.1:8080
MDU_ASSETS_DATADIRECTORY=/var/lib/mdu-assets/data
MDU_ASSETS_UIBASEURL=https://assets.example.com
MDU_ASSETS_PUBLICBASEURL=https://img.example.com
MDU_ASSETS_ADMIN_USER=admin
MDU_ASSETS_ADMIN_PASSWORD=choose-a-long-password
```

```bash
chmod 600 /etc/mdu-assets.env && systemctl daemon-reload && systemctl enable --now mdu-assets
```

```bash
curl -s http://127.0.0.1:8080/healthz
```

> [!RESULT]
> `Healthy`. `journalctl -u mdu-assets` shows the log, one JSON object per line.

Then point your reverse proxy at `127.0.0.1:8080` for both host names, with the original `Host` header and `X-Forwarded-Proto`. The `Caddyfile` in [Docker Compose](Docker-Compose.md#set-up) works as it is with `reverse_proxy 127.0.0.1:8080`. Remove the password from the environment file after the first sign-in.

## macOS

On a Mac the same archive runs from a terminal or as a `launchd` agent. A browser download marks the archive as quarantined; downloading with `curl` as above does not. The program is notarized, so Gatekeeper lets it start either way once the Mac can reach Apple's notarization service.

```bash
MDU_ASSETS_UIBASEURL=http://127.0.0.1:8080 MDU_ASSETS_PUBLICBASEURL=http://localhost:8080 \
MDU_ASSETS_ALLOWHTTP=true MDU_ASSETS_ADMIN_USER=admin MDU_ASSETS_ADMIN_PASSWORD=change-me-please \
ASPNETCORE_URLS=http://127.0.0.1:8080 ./mdu-assets
```

This starts a local instance for trying it out; data goes to `data` in the current folder.
