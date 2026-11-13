---
description: Public links consist of a random asset ID and a cosmetic file name.
---

# 0003 Public Link Format

- Status: Accepted
- Date: 2026-11-13

## Context

Links end up in Markdown files and must never change, even when an image is moved to another folder, its project is renamed or the image is replaced (quality goal link stability). Links should still be readable in Markdown.

## Options

- `/i/<id>/<file-name>` with a random ID
- `/p/<project>/<id>.<ext>` with the project in the path
- `/h/<content-hash>.<ext>` with a content hash

## Decision

`https://<public-host>/i/<id>/<file-name>`. The ID is at least 10 base62 characters from a cryptographic random generator and is the only part used for the lookup. The file name is cosmetic; any name and no name resolve to the same asset. Version links use `/i/<id>/v<n>/<file-name>`.

## Consequences

- Folders, projects, names and versions can change freely.
- Content hashes would change on every replacement, and project names would need redirects, so both were rejected.
- The file extension in an old link may differ from the served type after a format change; the `Content-Type` header is authoritative.
- IDs are never reused, also not after final deletion.
