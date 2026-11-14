---
navigation.order: 20
description: Run the MDU Asset Server on a VPS with Docker Compose and Caddy, with separate host names for the web interface and public links.
---

# Deployment

This guide sets up the reference installation from the [deployment view](Architecture/Deployment-View.md): one VPS, Docker Compose, Caddy as reverse proxy with automatic HTTPS, and two host names.

| Host name | Serves |
| --- | --- |
| `assets.example.com` | Web interface and API, behind a password |
| `img.example.com` | Public links `/i/…`, without a password |

Replace both with your own names. They must differ, so that uploaded files never run on the host of the web interface ([ADR-0009](Architecture/Architecture-Decisions/0009-Separate-Public-Host.md)).

## Requirements

- A Linux VPS (x64 or arm64) with Docker Engine and the Compose plugin
- Ports 80 and 443 reachable from the internet, so Caddy can get certificates
- DNS `A` (and `AAAA`) records for both host names pointing to the VPS
- Git, to build the image from the repository

> [!NOTE]
> Until the first release publishes `capjan/mdu-assets` on Docker Hub, the image is built on the server from the repository. Afterwards, replace `build:` with `image: capjan/mdu-assets:<version>`.

## Set up

Create a folder for the installation and clone the repository into it:

```bash title="On the VPS"
mkdir -p /opt/mdu-assets && cd /opt/mdu-assets
git clone https://github.com/capjan/mdu-assets.git source
```

Create `compose.yaml` next to it:

```yaml title="/opt/mdu-assets/compose.yaml"
services:
  mdu-assets:
    build: ./source/Source
    restart: unless-stopped
    environment:
      MDU_ASSETS_UIBASEURL: https://assets.example.com
      MDU_ASSETS_PUBLICBASEURL: https://img.example.com
      # Caddy reaches the server from this network; only it may report HTTPS and client addresses.
      MDU_ASSETS_TRUSTEDPROXIES: 172.30.0.0/24
      MDU_ASSETS_ADMIN_USER: ${ADMIN_USER}
      MDU_ASSETS_ADMIN_PASSWORD: ${ADMIN_PASSWORD}
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

Create `.env` with the first administrator. The password needs at least 12 characters; the variables are only read while the instance has no users:

```bash title="/opt/mdu-assets/.env"
ADMIN_USER=admin
ADMIN_PASSWORD=choose-a-long-password
```

```bash
chmod 600 /opt/mdu-assets/.env
```

## Start

```bash
cd /opt/mdu-assets && docker compose up -d --build
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

## Users and projects

Admins manage everything in the **Admin** area of the web interface:

- **Users:** create a user and hand over the start password that appears once. The user chooses an own password at the first sign-in; until then nothing else works. Lock a user to stop access at once, reset a password to issue a new start password, or make another user an admin. The last unlocked admin cannot lose the role or be locked.
- **Projects:** admins create projects with **New project** in the sidebar. To let every user create projects, turn on **All users may create projects** in the settings ([ADR-0014](Architecture/Architecture-Decisions/0014-Project-Creation-by-Admin-Setting.md)). Whoever creates a project becomes its owner.
- **Members:** owners add members in **Project settings** as viewer (look and copy), editor (also upload and change) or owner (also manage members, rename or delete the project). Admins have owner rights in every project without being members.

The short name of a project appears in API paths and never changes; renaming a project changes only its display name, and image links stay the same. Only empty projects can be deleted until the trash arrives.

## Upload from the command line

```bash
curl -u admin -H "Content-Type: image/png" --data-binary @screenshot.png \
  "https://assets.example.com/api/v1/projects/default/assets?name=screenshot.png"
```

The answer contains the public `url`, the `markdown` snippet, and width, height and size. Scripts running in a browser cannot change anything from another site: the server rejects such requests by their `Origin` and `Sec-Fetch-Site` headers.

## Update

```bash
cd /opt/mdu-assets/source && git pull && cd .. && docker compose up -d --build
```

The database schema is migrated on start. Public links never change between versions.

## Backup

All state is in the `data` volume. Until the backup command arrives (milestone M9), stop the server for a consistent copy:

```bash
cd /opt/mdu-assets && docker compose stop mdu-assets
```

```bash
docker run --rm -v mdu-assets_data:/data -v "$PWD":/backup alpine tar czf /backup/mdu-assets-data.tar.gz -C /data .
```

```bash
docker compose start mdu-assets
```

The folder `tmp/` in the volume can be left out.

## Sign-in

The web interface uses HTTP Basic authentication: the browser asks for user name and password on the first visit and sends them with every request. Two consequences until session sign-in arrives:

- Signing out means closing the browser or clearing its saved sign-in.
- Some embedded browsers, for example in-app browsers of editors, show no password prompt. Use a regular browser for the web interface.

Passwords are only accepted over HTTPS. A request over plain HTTP, or one that claims HTTPS through `X-Forwarded-Proto` from an address outside `MDU_ASSETS_TRUSTEDPROXIES`, gets `401`.

All settings are listed in the [deployment view](Architecture/Deployment-View.md#reference-installation-vps-with-docker).
