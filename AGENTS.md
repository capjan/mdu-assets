# Repository guide for agents

## Project purpose

The MDU Asset Server is a self-hosted image server for MDU documentation: authors drop or paste images and get stable links and ready-made Markdown snippets. The project is in planning; see `README.md` for the status.

## Where things are

- `Docs/Requirements.md` – requirements with IDs such as `LNK-01` (German).
- `Docs/Architecture` – arc42 architecture documentation (English), rendered with MDU. Section 9 holds the architecture decision records in `Architecture-Decisions/`.
- `Spikes/NetVipsAot` – spike that verifies NetVips/libvips under Native AOT; CI runs it on every target.
- Work is planned in GitHub milestones M0–M11 with one issue per feature.

## Keep the architecture documentation current

The arc42 documentation is maintained together with the code. In the same pull request as the code change:

- Update the arc42 section that the change affects: building blocks (5), runtime behavior (6), deployment or configuration (7), crosscutting concepts such as security, persistence, HTTP caching or UI (8), quality scenarios (10), risks and open questions (11), new terms (12).
- Record a hard-to-reverse decision as a new ADR from the template in `Docs/Architecture/Architecture-Decisions/README.md` and add it to the table there. Never rewrite an accepted ADR; supersede it with a new one and update both statuses.
- Replace "planned" descriptions with the actual state when the code lands.
- When a requirement changes or an open question (Q-…) is decided, update `Docs/Requirements.md` as well.
- Run `mdu lint Docs` and keep it free of errors. Link to files outside `Docs` with absolute GitHub URLs, because relative links leave the rendered site.

## Constraints

- .NET 10 with Native AOT; no trim or AOT warnings. Build, test and generator tooling must be .NET only: no Node.js or Python.
- Web interface from embedded static HTML, CSS and plain JavaScript; MDU design tokens and the MDU keyboard focus style.
- Target platforms: linux-x64, linux-arm64, osx-arm64. Windows is not a target.
- Host names are configuration, never code.
- Public links never change: do not alter the link format, ID generation or blob layout without an ADR.

## Working in this repository

- Changes to `main` go through pull requests with green CI; branches are deleted after merge.
- Commit messages and pull request titles follow Conventional Commits (`<type>[optional scope][!]: <description>`).
- Docker on macOS runs through Colima; start it with `colima start`. Do not build .NET under x64 emulation: the SDK crashes; build on native runners or cross-compile.
