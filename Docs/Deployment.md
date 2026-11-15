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

With [MDU](https://github.com/capjan/mdu) 6.10 or later, set `assets.server`, `assets.public` and `assets.project` in the site's `mdu.yml` and the token in `MDU_ASSETS_TOKEN`; then `mdu assets upload screenshot.png` prints the Markdown snippet, and `mdu lint` reports image links whose image is unknown or in the trash (`MDU032`). See [assets](https://capjan.github.io/mdu/Usage/assets.html) in the MDU documentation.

Without MDU, create a personal API token on the **Account** page; it can be limited to one project and to reading, and the page offers a ready ShareX configuration and this curl command for it:

```bash
curl -H "Authorization: Bearer mdua_…" -H "Content-Type: image/png" --data-binary @screenshot.png \
  "https://assets.example.com/api/v1/projects/default/assets?name=screenshot.png"
```

Uploads are converted to WebP: lossless for screenshots and graphics, lossy at high quality for photos. Add `&lossless=true` when every pixel matters; then nothing loses quality and JPEG stays as uploaded.

With a password instead of a token:

```bash
curl -u admin -H "Content-Type: image/png" --data-binary @screenshot.png \
  "https://assets.example.com/api/v1/projects/default/assets?name=screenshot.png"
```

The answer contains the public `url`, the `markdown` snippet, and width, height and size. The whole API is described at `https://assets.example.com/api/v1/openapi.json`. Scripts running in a browser cannot change anything from another site: the server rejects such requests by their `Origin` and `Sec-Fetch-Site` headers.

## Update

```bash
cd /opt/mdu-assets/source && git pull && cd .. && docker compose up -d --build
```

## Behind an existing Traefik

On a server where Traefik already serves other sites, the container joins Traefik's Docker network instead of bringing its own proxy. The production instance runs this way; its files are in [`Deploy/traefik`](https://github.com/capjan/mdu-assets/tree/main/Deploy/traefik):

| File | Purpose |
| --- | --- |
| `docker-compose.yml` | The service with Traefik labels: two routers, one per host name, both to port 8080, TLS from Traefik's certificate resolver |
| `deploy.sh` | Builds and starts one commit of `main`, then checks `/healthz` through Traefik |

Adjust the host names, the entry point (`websecure`), the certificate resolver (`mytlschallenge`) and the network (`web`) to your Traefik. `MDU_ASSETS_TRUSTEDPROXIES` is the subnet of that network (`docker network inspect web`); every container in it could claim HTTPS and client addresses, so keep only your own services there.

```bash title="On the VPS"
mkdir -m 700 ~/container/assets.example.com && cd ~/container/assets.example.com
git clone https://github.com/capjan/mdu-assets.git source
cp source/Deploy/traefik/docker-compose.yml source/Deploy/traefik/deploy.sh .
umask 077 && printf 'ADMIN_USER=admin\nADMIN_PASSWORD=%s\n' "$(head -c 24 /dev/urandom | base64)" > .env
./deploy.sh "$(git -C source rev-parse origin/main)"
```

After the first start, move the password out of `.env` (or delete it once you have signed in and changed it).

## Automatic deployment from GitHub

The workflow [`deploy.yml`](https://github.com/capjan/mdu-assets/blob/main/.github/workflows/deploy.yml) deploys every successful build of `main`. Because the repository is public, it is built so that neither pull requests nor a leaked key can run code of their choice on the server:

- It runs only after the **Build** workflow succeeded for a push to `main` of this repository, or when started by hand. Builds of pull requests and forks never deploy.
- Its secrets are in the GitHub environment `production`, which only the branch `main` may use.
- The SSH key can do one thing: in `~/.ssh/authorized_keys` it has `restrict` (no shell, no forwarding) and the forced command `deploy.sh`, which accepts only `deploy <commit sha>`.
- The server builds the image itself from its read-only checkout of the public repository, and only for commits that are on `main`. No registry or GitHub credentials are on the server, and no server credentials are in the image.
- The host key is pinned in a secret, so the workflow never trusts an unknown server.

Set it up once. Create a key without a passphrase on your computer, install its public half on the server, and store the rest with the GitHub CLI:

```bash title="On your computer"
ssh-keygen -t ed25519 -N '' -C 'github-actions mdu-assets deploy' -f deploykey
```

```bash title="~/.ssh/authorized_keys on the VPS, one line"
restrict,command="/home/user/container/assets.example.com/deploy.sh" ssh-ed25519 AAAA… github-actions mdu-assets deploy
```

```bash title="On your computer"
echo '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' \
  | gh api -X PUT repos/OWNER/mdu-assets/environments/production --input -
gh api -X POST repos/OWNER/mdu-assets/environments/production/deployment-branch-policies -f name=main -f type=branch
gh secret set DEPLOY_SSH_KEY --env production < deploykey
ssh-keyscan -t ed25519 vps.example.com | gh secret set DEPLOY_KNOWN_HOSTS --env production
printf 'user@vps.example.com' | gh secret set DEPLOY_TARGET --env production
gh variable set DEPLOY_URL --env production --body https://assets.example.com
rm deploykey deploykey.pub
```

Compare the `ssh-keyscan` fingerprint with `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` on the server before you store it. To deploy by hand, run **Deploy** in the Actions tab or `gh workflow run deploy.yml`. To revoke the key, delete its line in `authorized_keys`.

The database schema is migrated on start. Public links never change between versions.

## Backup

All state is in the `data` volume. `mdu-assets backup <folder>` writes a consistent copy while the server runs: the database through SQLite's online backup, then every file it refers to. The keys that protect sign-in sessions stay out on purpose; after a restore everybody signs in again. The target folder must be new or empty. In the container, run the program itself; the image has no shell:

```bash
cd /opt/mdu-assets && docker compose exec mdu-assets /app/mdu-assets backup /data/backups/$(date +%F)
```

Then move the copy off the server, for example to a storage box, and remove it from the volume:

```bash
docker run --rm -v mdu-assets_data:/data -v "$PWD":/backup alpine sh -c 'tar czf /backup/mdu-assets-$(date +%F).tar.gz -C /data/backups . && rm -rf /data/backups'
```

To restore, stop the server, replace the contents of the volume with a backup folder and start it again. Previews and resized copies are made again when they are requested.

## Moving projects

Owners export a project in **Project settings** as a zip with the current or all versions; admins (or everybody, when an admin allows creating projects) import it on **New project**. Image ids stay the same where the target server has not used them; the others get new ids, listed after the import ([ADR-0018](Architecture/Architecture-Decisions/0018-Import-Id-Collisions.md)).

Exports from servers that still had folders can be imported too: each image gets its folder and the folders above it as tags ([ADR-0020](Architecture/Architecture-Decisions/0020-Tags-Instead-of-Folders.md)). The update itself converts folders the same way.

## Sign-in

The web interface has its own sign-in page. After signing in, the browser keeps a session cookie: until it is closed, or for 14 days without use with **Stay signed in**. The user name in the header opens a menu with **Account**, **Admin** and **Sign out**. Locking a user, deleting a user or changing a password ends that user's sessions at once.

Tools such as curl and ShareX use a personal API token, or user name and password as HTTP Basic credentials. Browsers never get a Basic Auth prompt, and Basic credentials a browser still remembers from older versions are ignored.

Backups do not contain the keys that protect the session cookies, so after a restore everybody signs in again.

Passwords are only accepted over HTTPS. A sign-in or request over plain HTTP, or one that claims HTTPS through `X-Forwarded-Proto` from an address outside `MDU_ASSETS_TRUSTEDPROXIES`, is refused.

All settings are listed in the [deployment view](Architecture/Deployment-View.md#reference-installation-vps-with-docker).
