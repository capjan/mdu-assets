---
navigation.order: 10
description: Choose how to run the MDU Asset Server and prepare the two host names it needs.
---

# Installation

The server needs two host names: one for the web interface and the API, one for the public image links. They must differ, so that an uploaded file can never run on the host of the web interface and reach its cookies ([ADR-0009](../Architecture/Architecture-Decisions/0009-Separate-Public-Host.md)).

| Host name in this guide | Serves |
| --- | --- |
| `assets.example.com` | Web interface and API, after sign-in |
| `img.example.com` | Public links `/i/…`, without sign-in |

Replace both with your own names and point their DNS records to the server.

## Ways to run it

| Way | When |
| --- | --- |
| [Docker Compose with Caddy](Docker-Compose.md) | Recommended. A fresh server; Caddy gets the certificates |
| [Behind an existing Traefik](Traefik.md) | A server where Traefik already serves other sites, with automatic deployment from GitHub |
| [Native program](Native-Binaries.md) | No Docker; Linux x64, Linux arm64 or macOS on Apple silicon behind your own reverse proxy |

Every way ends the same: the server answers `Healthy` on `/healthz`, and you sign in with the first administrator.

## The first administrator

On the first start, while the database has no users, the server creates an administrator from `MDU_ASSETS_ADMIN_USER` and `MDU_ASSETS_ADMIN_PASSWORD`. The password needs at least 12 characters. Without the two variables, the first start stops with a message that names them. Later starts ignore them, so remove the password from the configuration once you have signed in.

## HTTPS

The server accepts passwords only over HTTPS. A reverse proxy terminates TLS and reports the original scheme with `X-Forwarded-Proto`; the server trusts that header only from loopback and the addresses in `MDU_ASSETS_TRUSTEDPROXIES`. See [Configuration](../Configuration.md).
