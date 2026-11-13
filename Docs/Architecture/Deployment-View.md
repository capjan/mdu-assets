---
navigation.order: 7
description: How the MDU Asset Server runs on a VPS with Docker behind a reverse proxy, and how its data directory is laid out.
---

# Deployment View

## Reference installation: VPS with Docker

The first production instance runs on a VPS with Docker Compose. A reverse proxy (Caddy in the reference setup) terminates TLS for two host names and forwards both to the same container.

```plantuml
@startuml
left to right direction
actor "Browsers and tools" as Users
node "VPS (Linux, Docker Compose)" {
  component "Caddy\nTLS for ui-host and public-host" as Caddy
  node "Container capjan/mdu-assets" {
    component "mdu-assets (Native AOT)" as App
    file "libvips" as Vips
  }
  storage "Volume /data" as Data
}
Users --> Caddy : HTTPS 443
Caddy --> App : HTTP 8080
App --> Vips
App --> Data
@enduml
```

| Element | Description |
| --- | --- |
| Reverse proxy | Obtains certificates, forwards both host names, sets `X-Forwarded-*`. The server trusts only configured proxy addresses. |
| Container | Image based on `runtime-deps` chiseled, non-root user, the AOT program and libvips. Built per architecture on native runners. |
| Volume `/data` | All state of the instance. Nothing else needs a backup. |
| Configuration | Environment variables `MDU_ASSETS_*`. Today: `MDU_ASSETS_DATADIRECTORY` (set to `/data` in the image) and the standard `ASPNETCORE_HTTP_PORTS` (`8080`). Later: UI base URL, public base URL, trusted proxies, limits, first admin. |
| Health check | `GET /healthz` answers `Healthy` when the database answers. The chiseled image has no shell or curl, so container health checks run from outside, for example from the reverse proxy or monitoring. |

The host names are configuration; the documentation uses `assets.example.com` for the UI and `img.example.com` for public links as placeholders.

## Data directory

```text
/data
├── mdu-assets.db         # SQLite database (WAL mode): users, projects, assets, versions, statistics, audit log
├── blobs/ab/cd/<sha256>  # content of every version, written once
├── variants/…            # resized and converted variants, can be deleted at any time
└── tmp/                  # uploads in progress
```

## Backup and restore

`mdu-assets backup <target>` writes a consistent copy of the database and the blobs. Where the copy goes (storage box, snapshot, other host) is the operator's choice. `variants/` and `tmp/` are not needed for a restore.

## Other ways to run

| Option | Notes |
| --- | --- |
| Native binary | Archive with the program, libvips and license texts for linux-x64, linux-arm64 and osx-arm64; macOS builds signed and notarized. |
| Local development | `dotnet run --project MduAssets` in `Source` listens on `http://127.0.0.1:5180` and keeps its data in `Source/MduAssets/data/` (ignored by Git). Later, both host names use `127.0.0.1` on different ports. |
| Image build | `docker build -t mdu-assets .` in `Source` builds for the architecture of the Docker host. |
