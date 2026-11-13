---
description: Anyone who knows a public link can load the image.
---

# 0004 Public Links Without Authentication

- Status: Accepted
- Date: 2026-11-13

## Context

MDU sites are static and often public (GitHub Pages and similar). Images on them must load in any reader's browser without a sign-in.

## Options

- Public links for everyone who knows them
- Public or private per project
- Always protected, with signed expiring URLs

## Decision

Public links need no authentication. Unguessable IDs ([ADR-0003](0003-Public-Link-Format.md)) and rate-limited 404s make guessing impractical.

## Consequences

- Works with every static host and Markdown viewer.
- A shared link cannot be revoked except by deleting the image; the UI and documentation say so.
- Private images are out of scope for now; a later per-project setting would need signed URLs and does not fit public sites.
