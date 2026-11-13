---
navigation.order: 1
description: Purpose of the MDU Asset Server, its ranked quality goals and its stakeholders.
---

# Introduction and Goals

## Requirements overview

The MDU Asset Server is a self-hosted web application that stores images for [MDU](https://github.com/capjan/mdu) documentation. Its job is to make the way from an image to a working Markdown link as short as possible and to keep that link working for good.

Core use cases:

| Use case | Requirements |
| --- | --- |
| Paste or drop an image and copy a ready-made Markdown snippet | UP-01 – UP-05, LNK-05 – LNK-08 |
| See and organize all images of a project in folders and tags without changing links | ORG-01 – ORG-06, LNK-01 |
| Replace an image under the same link and restore earlier versions | VER-01 – VER-03 |
| Work together in projects; admins manage all projects and users | PRJ-01 – PRJ-05, AUTH-04 |
| Upload and query images through an API from external tools | API-01 – API-07, AUTH-06 |
| See how often and where an image is requested | STA-01 – STA-04 |
| Upload from and check links with the `mdu` CLI | CLI-01 – CLI-03 |

The full list is in the [requirements document](../Requirements.md) (German).

## Quality goals

Ranked by priority. When two goals conflict, the higher one wins.

| Rank | Quality goal | Meaning |
| --- | --- | --- |
| 1 | **Usability** | From a screenshot to a Markdown snippet in a few seconds, without thinking about where the image goes or how the link looks. |
| 2 | **Link stability** | A published link keeps working across moves, renames, new versions, updates and migrations of the server. |
| 3 | **Security** | Uploads can never run script in a browser, permissions are enforced on every request, and no personal data leaks through images or statistics. |
| 4 | **Operational simplicity** | One container, one data volume, backup by copying, small resource needs. |

Usability ranks first on purpose: a server that is awkward to use will not be used, and then its links do not matter. Security is not traded away lightly, though: when usability and security conflict, the solution must keep both, for example by making the safe path the convenient one.

## Stakeholders

| Stakeholder | Expectations |
| --- | --- |
| Documentation authors (a small team, up to about 10 users) | Fast upload by paste and drop, snippets that work in MDU, finding images again |
| Project owners | Control over members and roles, overview of storage and usage |
| Administrator and operator | Simple installation on a VPS, user management, backups, few moving parts |
| Readers of MDU sites | Images load fast and reliably; no tracking |
| Authors of upload tools and scripts | A documented, stable API with tokens |
| Maintainers of MDU | A clear contract for `mdu assets upload` and the link check in `mdu lint`; consistent design |
| Contributors | Readable code, .NET-only tooling, documented decisions |
