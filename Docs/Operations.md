---
navigation.order: 50
description: Update the server, back it up and restore it, check its health, and move projects between servers.
---

# Operations

All state lives in the data folder: the SQLite database, the stored files and the keys that protect sign-in sessions. The image keeps it in the volume mounted at `/data`.

## Update

Read the [release notes](https://github.com/capjan/mdu-assets/releases) first; breaking changes are listed at the top. The database schema is migrated on start, and public links never change between versions.

With [Docker Compose](Installation/Docker-Compose.md), change the version of the image in `compose.yaml`, then:

```bash
cd /opt/mdu-assets && docker compose pull && docker compose up -d
```

Built from source:

```bash
cd /opt/mdu-assets/source && git pull && cd .. && docker compose up -d --build
```

As a [native program](Installation/Native-Binaries.md), extract the new archive next to the old one, move the link, and restart:

```bash
ln -sfn mdu-assets-1.1.0-linux-x64 /opt/mdu-assets && systemctl restart mdu-assets
```

Back up before an update; a newer schema cannot be opened by an older program.

## Health and logs

`/healthz` on the UI host answers `Healthy` when the server and its database work. Use it for monitoring and container health checks.

The log goes to standard output, one JSON object per line. Each request is logged with method, path, status and duration; failed sign-ins and refused requests are logged as warnings.

## Backup

`mdu-assets backup <folder>` writes a consistent copy while the server runs: the database through SQLite's online backup, then every file it refers to. The target folder must be new or empty. The keys that protect sign-in sessions stay out on purpose, so after a restore everybody signs in again.

In the container, run the program itself; the image has no shell:

```bash
cd /opt/mdu-assets && docker compose exec mdu-assets /app/mdu-assets backup /data/backups/$(date +%F)
```

Then move the copy off the server, for example to a storage box, and remove it from the volume:

```bash
docker run --rm -v mdu-assets_data:/data -v "$PWD":/backup alpine sh -c 'tar czf /backup/mdu-assets-$(date +%F).tar.gz -C /data/backups . && rm -rf /data/backups'
```

As a native program, run it with the same environment as the service:

```bash
sudo -u mdu-assets env $(cat /etc/mdu-assets.env | xargs) /opt/mdu-assets/mdu-assets backup /var/lib/mdu-assets/backup-$(date +%F)
```

## Restore

Stop the server, replace the contents of the data folder with a backup folder, and start it again. Previews and resized copies are made again when they are requested.

## Move a project to another server

Owners export a project in **Project settings** as a zip with the current or all versions and the metadata. On the other server, administrators (or everybody, when an administrator allows creating projects) import it with **Import project** on **New project**.

Image ids stay the same where the target server has not used them, so links keep working once the public host name points to the new server. Ids that are taken get new ones, listed after the import ([ADR-0018](Architecture/Architecture-Decisions/0018-Import-Id-Collisions.md)).

Exports from versions that still had folders can be imported too: each image gets its folder and the folders above it as tags ([ADR-0020](Architecture/Architecture-Decisions/0020-Tags-Instead-of-Folders.md)).
