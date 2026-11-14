---
navigation.order: 9
description: Architecture decision records of the MDU Asset Server.
---

# Architecture Decisions

Each significant, hard-to-reverse decision is recorded as one file in the [MADR](https://adr.github.io/madr/) style: context, considered options, decision and consequences. Records are not rewritten later; a new record supersedes an old one and both say so.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-Record-Architecture-Decisions.md) | Record architecture decisions as MADR files | Accepted |
| [0002](0002-Native-AOT-Minimal-APIs.md) | .NET 10 Native AOT with ASP.NET Core minimal APIs | Accepted |
| [0003](0003-Public-Link-Format.md) | Public links from a random asset ID and a cosmetic file name | Accepted |
| [0004](0004-Public-Links-Without-Authentication.md) | Public links need no authentication | Accepted |
| [0005](0005-SQLite-and-Content-Addressed-Files.md) | SQLite for metadata, content-addressed files on disk | Accepted |
| [0006](0006-NetVips-for-Image-Processing.md) | NetVips/libvips for image processing | Accepted |
| [0007](0007-Static-UI-on-the-JSON-API.md) | Web interface as embedded static files on the JSON API | Accepted |
| [0008](0008-Basic-Auth-and-API-Tokens.md) | Basic Auth first, personal API tokens for tools | Accepted |
| [0009](0009-Separate-Public-Host.md) | Serve public links from a separate host | Accepted |
| [0010](0010-MDU-Design-Tokens.md) | Copy the MDU design tokens with a drift test | Accepted |
| [0011](0011-AVIF-in-the-Background.md) | Encode AVIF variants in the background | Accepted |
| [0012](0012-Own-SVG-Sanitizer.md) | Clean SVG uploads with an own sanitizer that follows MDU's allowlist | Accepted |
| [0013](0013-Image-Headers-Without-an-Image-Library.md) | Read image type and size from headers without an image library | Accepted |
| [0014](0014-Project-Creation-by-Admin-Setting.md) | Admins create projects; other users only when an admin allows it | Accepted |

## Template

```markdown
# NNNN Title

- Status: Proposed | Accepted | Superseded by [NNNN](NNNN-….md)
- Date: YYYY-MM-DD

## Context

## Options

## Decision

## Consequences
```
