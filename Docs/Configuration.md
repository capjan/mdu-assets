---
navigation.order: 20
description: All settings of the MDU Asset Server, as environment variables or in appsettings.json, with their defaults.
---

# Configuration

Settings come from environment variables with the prefix `MDU_ASSETS_` or from `appsettings.json` next to the program. Names are not case-sensitive: `MDU_ASSETS_PUBLICBASEURL` and `"PublicBaseUrl"` are the same setting. Environment variables win over the file.

A wrong value stops the start with a message that names the setting, for example `mdu-assets cannot start: MDU_ASSETS_THUMBNAILSIZE must be between 64 and 2048.`

## Required

| Setting | Meaning |
| --- | --- |
| `MDU_ASSETS_UIBASEURL` | Base URL of the web interface and the API, for example `https://assets.example.com` |
| `MDU_ASSETS_PUBLICBASEURL` | Base URL of public links, for example `https://img.example.com`. Must use a different host than the web interface |
| `MDU_ASSETS_ADMIN_USER`, `MDU_ASSETS_ADMIN_PASSWORD` | The first administrator. Only read while the database has no users; the password needs at least 12 characters |

## Server and network

| Setting | Default | Meaning |
| --- | --- | --- |
| `MDU_ASSETS_DATADIRECTORY` | `data` | Folder for the database and all files. Relative paths start at the working directory. The image uses `/data` |
| `MDU_ASSETS_TRUSTEDPROXIES` | (none) | Comma-separated addresses or networks (CIDR) of reverse proxies whose `X-Forwarded-*` headers are trusted. Loopback is always trusted |
| `MDU_ASSETS_ALLOWHTTP` | `false` | Accept passwords over plain HTTP. Only for local tests |
| `ASPNETCORE_URLS` or `ASPNETCORE_HTTP_PORTS` | `8080` in the image | Address the server listens on, for example `http://127.0.0.1:8080` |

## Uploads and images

| Setting | Default | Meaning |
| --- | --- | --- |
| `MDU_ASSETS_MAXUPLOADBYTES` | `52428800` (50 MB) | Largest accepted upload. Projects can set a lower limit |
| `MDU_ASSETS_MAXPIXELS` | `50000000` | Largest image in pixels (width × height), checked before decoding |
| `MDU_ASSETS_WIDTHS` | `320,640,800,1024,1280,1600,2048` | Widths that `?w=` may ask for; other values snap to the next larger one |
| `MDU_ASSETS_THUMBNAILSIZE` | `480` | Longer side of the previews in the web interface, 64 to 2048 pixels |
| `MDU_ASSETS_IMAGEJOBS` | `0` | Image jobs running at the same time; `0` means half the processors |
| `MDU_ASSETS_MAXIMPORTBYTES` | `2147483648` (2 GB) | Largest project export accepted for import |

## Public links

| Setting | Default | Meaning |
| --- | --- | --- |
| `MDU_ASSETS_PUBLICMAXAGESECONDS` | `300` | How long browsers and caches may reuse a public link without asking again |
| `MDU_ASSETS_PUBLICSTALEWHILEREVALIDATESECONDS` | `86400` | How long caches may serve a stale copy while they revalidate it |

Version links (`/i/<id>/v<n>/<name>`) never change and are always served as `immutable`.

## Statistics, limits and retention

| Setting | Default | Meaning |
| --- | --- | --- |
| `MDU_ASSETS_STATSFLUSHSECONDS` | `10` | How often counted requests are written to the database, 1 to 3600 seconds |
| `MDU_ASSETS_BOTUSERAGENTS` | `bot,crawl,spider,…` | Comma-separated parts of user agents that are not counted |
| `MDU_ASSETS_SIGNINFAILURES` | `10` | Failed sign-ins per user name and per address within 15 minutes before further tries are refused |
| `MDU_ASSETS_UPLOADSPERMINUTE` | `60` | Uploads and new versions per user and minute |
| `MDU_ASSETS_PUBLICNOTFOUNDPERMINUTE` | `100` | `404` answers on public links per address and minute before further requests get `429` |
| `MDU_ASSETS_TRASHRETENTIONDAYS` | `30` | Days deleted images stay in the trash before they are deleted for good |
| `MDU_ASSETS_AUDITRETENTIONDAYS` | `365` | Days audit log entries are kept |

## Logging

The server writes one JSON object per line to standard output, with UTC timestamps, ready for `docker compose logs`, journald or a log collector. Log levels follow the usual .NET settings, for example `MDU_ASSETS_LOGGING__LOGLEVEL__DEFAULT=Warning`.

## In the web interface

Some settings live in the database and are changed by administrators and owners in the web interface:

- **All users may create projects** (admin settings)
- Per project: the largest upload, the storage quota, how many old versions are kept, and whether uploads are converted to WebP ([Administration](Administration.md))
