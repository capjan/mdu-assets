---
navigation.order: 4
description: The key approaches with which the MDU Asset Server reaches its quality goals.
---

# Solution Strategy

| Quality goal | Approach |
| --- | --- |
| Usability | Upload by paste and drop anywhere in the project view; snippet ready and copied right after upload; alt text suggested from the file name; snippets for MDU features (light/dark pairs, frame, caption); keyboard shortcuts; integrations for `mdu` and upload tools. |
| Link stability | Links contain only a random asset ID and a cosmetic file name ([ADR-0003](Architecture-Decisions/0003-Public-Link-Format.md)). Folders, projects, names and versions live in metadata. Replacing creates a new version under the same link. Deletion goes to a trash first. Migrations never change IDs; export and import keep them. |
| Security | Public host separate from the UI host ([ADR-0009](Architecture-Decisions/0009-Separate-Public-Host.md)); file type from content; SVG sanitized and served with a sandboxing CSP; metadata stripped from photos; roles checked in every endpoint; API tokens instead of passwords in tools; rate limiting; no personal data in statistics. |
| Operational simplicity | One Native AOT process with SQLite and files in one data directory ([ADR-0005](Architecture-Decisions/0005-SQLite-and-Content-Addressed-Files.md)); one container; configuration through environment variables; backup command. |

## Technology decisions

| Topic | Decision |
| --- | --- |
| Platform | .NET 10 Native AOT with ASP.NET Core minimal APIs ([ADR-0002](Architecture-Decisions/0002-Native-AOT-Minimal-APIs.md)) |
| Persistence | SQLite with hand-written SQL and versioned migrations; files content-addressed on disk ([ADR-0005](Architecture-Decisions/0005-SQLite-and-Content-Addressed-Files.md)) |
| Images | NetVips/libvips ([ADR-0006](Architecture-Decisions/0006-NetVips-for-Image-Processing.md)); AVIF in the background ([ADR-0011](Architecture-Decisions/0011-AVIF-in-the-Background.md)) |
| Web interface | Embedded static files with plain JavaScript on the JSON API ([ADR-0007](Architecture-Decisions/0007-Static-UI-on-the-JSON-API.md)) |
| Design | MDU design tokens copied with a drift test ([ADR-0010](Architecture-Decisions/0010-MDU-Design-Tokens.md)) |
| Authentication | Basic Auth first, personal API tokens for tools ([ADR-0008](Architecture-Decisions/0008-Basic-Auth-and-API-Tokens.md)) |

## Organization of the work

The server grows in [milestones](https://github.com/capjan/mdu-assets/milestones): first an empty AOT skeleton, then a thin path from upload to working link, then a usable interface that runs on a real instance for daily use. Later milestones widen the system feature by feature. This proves the risky parts (AOT, native image library, deployment) first and lets real use steer the order of the rest.
