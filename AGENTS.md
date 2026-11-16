# Repository guide for agents

## Project purpose

The MDU Asset Server is a self-hosted image server for MDU documentation: authors drop or paste images and get stable links and ready-made Markdown snippets. The project is in planning; see `README.md` for the status.

## Where things are

- `Docs/Requirements.md` – requirements with IDs such as `LNK-01`.
- `Docs/Architecture` – arc42 architecture documentation (English), rendered with MDU. Section 9 holds the architecture decision records in `Architecture-Decisions/`.
- `Docs/Architecture/NetVips-AOT-Spike.md` – report of the spike that verifies NetVips/libvips under Native AOT.
- `Source` – everything that compiles. `Source/Spikes/NetVipsAot` holds the spike code; CI runs it on every target.

Keep this split: documentation only in `Docs` (plus the root README, AGENTS.md and LICENSE), code and build files only in `Source`. The build workflow (`.github/workflows/build.yml`) runs only for changes under `Source/` (its `paths` rules), so build files such as a solution, `global.json` or `Directory.Build.props` belong in `Source/` too.
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
- Commit messages and pull request titles follow Conventional Commits (`<type>[optional scope][!]: <description>`). `Source/cliff.toml` turns them into versions: `feat` bumps minor, `fix` and `perf` bump patch, breaking changes bump major, other types do not bump.
- Start releases manually from `main` with the `Release` workflow (`.github/workflows/release.yml`). It calculates the version and notes with git-cliff, builds and smoke-tests the native archives and the Docker images, creates the GitHub release and then tags `capjan/mdu-assets:<version>` and `latest`. Do not hard-code a version in project files.
- The release needs these repository settings, checked in its first job: secrets `APPLE_DEVELOPER_ID_APPLICATION_CERTIFICATE_BASE64` (base64-encoded Developer ID Application `.p12`), `APPLE_DEVELOPER_ID_APPLICATION_CERTIFICATE_PASSWORD`, `APPLE_NOTARIZATION_PRIVATE_KEY` (App Store Connect API key `.p8`, Developer role or higher), `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` (read and write access token); variables `APPLE_NOTARIZATION_KEY_ID` and `APPLE_NOTARIZATION_ISSUER_ID`; and the public Docker Hub repository `capjan/mdu-assets`.
- Releases ship third-party notices and license texts: `Source/THIRD-PARTY-NOTICES.md` and `Source/Licenses`, plus the libvips notices that the release workflow and the Dockerfile copy from the NetVips.Native package. When a release starts to ship another third-party library, add it there. macOS signing in the release workflow signs every `.dylib` in the archive.
- Docker on macOS runs through Colima; start it with `colima start`. Do not build .NET under x64 emulation: the SDK crashes; build on native runners or cross-compile.
