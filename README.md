# MDU Asset Server

A self-hosted image server for [MDU](https://github.com/capjan/mdu) documentation. Drop or paste an image, get a stable link, and paste the ready-made Markdown into your docs.

> **Status:** Milestone M2. A web interface in the MDU style uploads images by drag and drop, paste or file picker and hands out Markdown snippets; images are served under stable public links. See the [deployment guide](Docs/Deployment.md) to run it. Progress is tracked in the [milestones](https://github.com/capjan/mdu-assets/milestones).

## Why

MDU sites are plain Markdown. Local images work well, but screenshots that change often, images shared by several documentation projects, or images added from tools outside the repository are easier to manage in one place. The asset server keeps those images, hands out links that never change, and shows how often each image is used, while the Markdown stays portable: an asset link is an ordinary HTTPS URL.

## Planned features

- **Upload by drag and drop or paste.** Paste a screenshot with <kbd>Cmd</kbd>+<kbd>V</kbd> and copy the Markdown snippet right away.
- **Stable links.** `https://assets.example.com/i/k3F9xQ/architecture.png` keeps working when the image moves to another folder, is renamed, or is replaced by a new version.
- **MDU-ready snippets.** Markdown with alt text, light and dark pairs (`#only-light` / `#only-dark`), and MDU attributes such as `{.image-frame}`.
- **Projects and folders.** Group images in nested folders and tags; several users work together with Owner, Editor, and Viewer roles. Admins see all projects and manage users.
- **Versions and trash.** Replace an image under the same link, restore earlier versions, and recover deleted images for 30 days.
- **Image processing.** Metadata stripping, thumbnails, resized variants (`?w=800`), and WebP/AVIF delivery.
- **Usage statistics.** Number of requests, last request, and referring sites for each image, without bots and without storing personal data.
- **API.** Upload and query images with personal API tokens, for example from ShareX or `curl`. The API is described as OpenAPI.
- **MDU integration.** `mdu assets upload` prints the snippet, and `mdu lint` reports links to deleted images.
- **MDU look.** The web interface uses the design tokens and components of MDU, including light and dark mode and keyboard focus styles.

## Technology

- .NET 10 with Native AOT, ASP.NET Core minimal APIs, SQLite, and files on disk
- Image processing with [NetVips](https://github.com/kleisauke/net-vips) (libvips)
- Web interface as embedded static HTML, CSS, and plain JavaScript; no Node.js toolchain
- Distributed as a multi-architecture Docker image and as native binaries for Linux and macOS

## Repository layout

| Path | Contents |
| --- | --- |
| [`Docs`](Docs) | All documentation, rendered with MDU: [requirements](Docs/Requirements.md) (German), [arc42 architecture](Docs/Architecture/README.md) with decisions, and the [NetVips AOT spike report](Docs/Architecture/NetVips-AOT-Spike.md) (German) |
| [`Source`](Source) | Everything that compiles: the server, its tests, the token sync tool and the [NetVips AOT spike](Source/Spikes/NetVipsAot) |

The build workflow runs only when something under `Source/` changes.

## Build and run

Requires the .NET 10 SDK, plus `clang` and `zlib1g-dev` on Linux for Native AOT.

Run the commands in `Source`, where `global.json` selects the .NET 10 SDK:

```sh
cd Source
dotnet test --solution MduAssets.slnx
dotnet run --project MduAssets
```

The development server serves the web interface and API on `http://127.0.0.1:5180` and public links on `http://127.0.0.1:5181`, keeps its data in `Source/MduAssets/data/` (ignored by Git) and creates the user `admin` with the password `development-only`. To update the copy of the MDU design tokens, run `dotnet run --project Tools/SyncTokens -- <path to mdu-design-system>`. Native AOT build and container, also in `Source`:

```sh
dotnet publish MduAssets -c Release -r osx-arm64 -o out
docker build -t mdu-assets .
docker run --rm -p 127.0.0.1:8080:8080 -v mdu-assets-data:/data mdu-assets
```

The [NetVips AOT spike](Docs/Architecture/NetVips-AOT-Spike.md) is published and run the same way in `Source/Spikes/NetVipsAot`; its exit code is the number of failed checks.

## Contributing

Commit messages and pull request titles follow [Conventional Commits](https://www.conventionalcommits.org/) (`<type>[optional scope][!]: <description>`). Changes to `main` go through pull requests; CI lints the documentation and runs the spike on every supported platform.

## License

[MIT](LICENSE)
