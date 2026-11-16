---
navigation.order: 10
description: Run the MDU Asset Server with Docker Compose and Caddy, which gets the certificates for both host names.
---

# Docker Compose

This is the reference installation from the [deployment view](../Architecture/Deployment-View.md): one Linux server, Docker Compose, the image `capjan/mdu-assets` and Caddy as reverse proxy with automatic HTTPS.

## Requirements

- A Linux server (x64 or arm64) with Docker Engine and the Compose plugin
- Ports 80 and 443 reachable from the internet, so Caddy can get certificates
- DNS `A` (and `AAAA`) records for both host names pointing to the server

## Set up

Create a folder for the installation:

```bash title="On the server"
mkdir -p /opt/mdu-assets && cd /opt/mdu-assets
```

Create `compose.yaml`. Pin the image to a version from the [releases](https://github.com/capjan/mdu-assets/releases), so updates happen when you choose:

```yaml title="/opt/mdu-assets/compose.yaml"
services:
  mdu-assets:
    image: capjan/mdu-assets:1.0.0
    restart: unless-stopped
    environment:
      MDU_ASSETS_UIBASEURL: https://assets.example.com
      MDU_ASSETS_PUBLICBASEURL: https://img.example.com
      # Caddy reaches the server from this network; only it may report HTTPS and client addresses.
      MDU_ASSETS_TRUSTEDPROXIES: 172.30.0.0/24
      MDU_ASSETS_ADMIN_USER: ${ADMIN_USER:-}
      MDU_ASSETS_ADMIN_PASSWORD: ${ADMIN_PASSWORD:-}
    volumes:
      - data:/data
    networks: [internal]

  caddy:
    image: caddy:2
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
      - "443:443/udp"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data
      - caddy-config:/config
    networks: [internal]

networks:
  internal:
    ipam:
      config:
        - subnet: 172.30.0.0/24

volumes:
  data:
  caddy-data:
  caddy-config:
```

Create `Caddyfile`:

```text title="/opt/mdu-assets/Caddyfile"
assets.example.com, img.example.com {
    encode zstd gzip
    reverse_proxy mdu-assets:8080
}
```

Caddy passes the original `Host` header and sets `X-Forwarded-Proto`, so the server knows which host was asked and that the request came over HTTPS.

Create `.env` with the [first administrator](README.md#the-first-administrator):

```bash title="/opt/mdu-assets/.env"
ADMIN_USER=admin
ADMIN_PASSWORD=choose-a-long-password
```

```bash
chmod 600 /opt/mdu-assets/.env
```

## Start

```bash
cd /opt/mdu-assets && docker compose up -d
```

```bash
docker compose logs mdu-assets
```

> [!RESULT]
> The log shows `Created the first administrator admin` and the two base URLs. A configuration error stops the container with a message such as `mdu-assets cannot start: MDU_ASSETS_PUBLICBASEURL is missing.`

Check both hosts:

```bash
curl -s https://assets.example.com/healthz
```

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://img.example.com/i/0000000000
```

The first answers `Healthy`, the second `404`. Then open `https://assets.example.com/` in a browser, sign in, and paste or drop an image.

After the first start, remove the password from `.env`; the account lives in the database now.

## Build from source instead

To run an unreleased commit, clone the repository next to `compose.yaml` and replace `image:` with a build:

```bash
git clone https://github.com/capjan/mdu-assets.git source
```

```yaml
    build: ./source/Source
```

Then start with `docker compose up -d --build`. Updates follow in [Operations](../Operations.md#update).
