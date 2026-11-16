---
description: Documentation of the MDU Asset Server, a self-hosted image server for MDU documentation.
---

# MDU Asset Server

The MDU Asset Server stores images for [MDU](https://github.com/capjan/mdu) documentation and hands out links that never change. Authors drop or paste an image, copy the ready-made Markdown, and paste it into their pages. Replacing the image later keeps the link, so every page shows the new version without an edit.

It is one small program for your own server: Native AOT, SQLite, no external services. The web interface and the API run on one host name, the public image links on a second one.

| Area | Contents |
| --- | --- |
| [Installation](Installation/README.md) | Run the server with Docker Compose, behind an existing Traefik, or as a native program |
| [Configuration](Configuration.md) | Every setting with its default |
| [Usage](Usage/README.md) | Upload, link, tag, find, replace and delete images in the web interface |
| [Administration](Administration.md) | Users, projects, members, limits and the audit log |
| [Operations](Operations.md) | Update, back up, restore and move projects between servers |
| [API](API.md) | The JSON API for scripts and tools |
| [Integrations](Integrations.md) | `mdu assets upload`, `mdu lint`, ShareX and curl |
| [Architecture](Architecture/README.md) | arc42 architecture documentation with the architecture decisions |
| [Requirements](Requirements.md) | Requirements from the initial interview |

## In a minute

```bash title="Try it locally"
docker run --rm -p 127.0.0.1:8080:8080 \
  -e MDU_ASSETS_UIBASEURL=http://127.0.0.1:8080 \
  -e MDU_ASSETS_PUBLICBASEURL=http://localhost:8080 \
  -e MDU_ASSETS_ALLOWHTTP=true \
  -e MDU_ASSETS_ADMIN_USER=admin \
  -e MDU_ASSETS_ADMIN_PASSWORD=change-me-please \
  capjan/mdu-assets
```

Open `http://127.0.0.1:8080/`, sign in as `admin`, and paste a screenshot with ++cmd+v++ or ++ctrl+v++. The two base URLs differ only in the host name (`127.0.0.1` and `localhost`), which is enough for a test. `MDU_ASSETS_ALLOWHTTP` lets passwords through plain HTTP and is only for trying the server out; for a real installation, follow [Installation](Installation/README.md).
