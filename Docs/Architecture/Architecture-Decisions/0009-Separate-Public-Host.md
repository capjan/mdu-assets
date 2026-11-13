---
description: Public links are served from a host name of their own, separate from the UI and API.
---

# 0009 Separate Public Host

- Status: Accepted
- Date: 2026-11-13

## Context

Users upload files that browsers render directly, including SVG. If such a file ever ran script on the UI host, it could act with the signed-in user's rights.

## Options

- One host for UI, API and public links
- A separate public host, both configured per installation

## Decision

Two configurable base URLs: the UI host serves the interface and the API, the public host serves only `/i/…`. Generated links always use the public host. Host names are configuration, never code.

## Consequences

- Served files have no access to credentials or cookies of the UI host.
- Installations need two DNS names and certificates; the reverse proxy forwards both to the same container.
- Local development uses two ports on `127.0.0.1`.
