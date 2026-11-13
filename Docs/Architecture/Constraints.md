---
navigation.order: 2
description: Technical, organizational and convention constraints of the MDU Asset Server.
---

# Constraints

## Technical constraints

| Constraint | Background |
| --- | --- |
| .NET 10, published with Native AOT | Small, fast-starting binaries and containers; same platform as MDU. Rules out Razor Pages, MVC, reflection-based JSON and EF Core runtime model building. |
| .NET-only build, test and generator tooling | Same rule as in the MDU repository: no Node.js or Python toolchain. Git, GitHub, OS tools, browsers and signing tools are allowed. |
| Web interface from embedded static files | HTML, CSS and plain JavaScript are embedded in the server. No CDNs, no external fonts. |
| Target platforms linux-x64, linux-arm64, osx-arm64 | Windows is currently not a target. |
| Image formats limited to the MDU image formats | png, jpg/jpeg, gif, webp, avif, bmp, svg. |
| Native image library via NetVips/libvips | Verified under Native AOT in the [NetVips AOT spike](https://github.com/capjan/mdu-assets/blob/main/Spikes/NetVipsAot/README.md); libvips ships as a native library next to the program. |

## Organizational constraints

| Constraint | Background |
| --- | --- |
| Open source under the MIT license | Public repository `capjan/mdu-assets`. |
| Changes to `main` only through pull requests with green CI | Branch protection with required checks, linear history. |
| Conventional Commits for commit messages and pull request titles | git-cliff derives versions and release notes, as in MDU. |
| Small team | Few moving parts; no infrastructure that needs a dedicated operator. |
| Work in milestones | Tracked as GitHub milestones and issues; each milestone ends in something usable. |

## Conventions

| Convention | Background |
| --- | --- |
| Design tokens and components of MDU | The interface looks like an MDU site, including light and dark mode and the MDU keyboard focus style. |
| English user interface and architecture documentation | Consistent with MDU. The requirements document is German. |
| Markdown stays portable | An asset link is an ordinary HTTPS URL; nothing in the Markdown depends on the asset server being MDU-specific. |
| Host names are configuration | No host name is hard-coded; the UI host and the public link host are set per installation. |
