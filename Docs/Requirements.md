# MDU Asset Server – Requirements

As of: 2026-11-13 · Status: draft after the requirements interview · Working name: `mdu-assets`

## 1. Goal

The MDU Asset Server is a self-hosted web application where authors of MDU documentation upload images and immediately get a permanent link that can be pasted straight into Markdown. It is built as a .NET Native AOT application and uses the MDU design system, so it looks like an MDU site.

The server complements local images in MDU projects; it does not replace them. Markdown files stay portable: an asset link is an ordinary HTTPS URL that any Markdown viewer can display.

## 2. Terms

| Term | Meaning |
| --- | --- |
| Asset | An uploaded image with all its versions, metadata and statistics |
| Asset ID | Short, random, unguessable ID (Base62) that identifies the asset permanently |
| Public link | `https://<public-host>/i/<asset-id>/<file-name>` – never changes |
| Version | One concrete file content of an asset; new versions replace the content under the same link |
| Project | Container for assets, with its own members |
| Tag | Free keyword on an asset; an asset can have several. Organizes and filters, does not affect the link. Tags replace the former folders ([ADR-0020](Architecture/Architecture-Decisions/0020-Tags-Instead-of-Folders.md)). |
| Snippet | Ready-to-copy text for an asset, such as a Markdown image with alt text |
| Light/dark pair | Two assets linked as the light and dark variant of the same image |

## 3. Decisions from the interview

| Topic | Decision |
| --- | --- |
| Link format | Short ID + readable file name: `/i/k3F9xQ/architecture.png`; `/i/k3F9xQ` returns the same file |
| Visibility | Images are public to anyone who knows the link; IDs are unguessable |
| Replacing | New version under the same link, version history with restore |
| Storage | File system for files, SQLite for metadata, one volume |
| Project roles | Owner / Editor / Viewer |
| API sign-in | Personal API tokens (named, with expiry, optionally limited to one project, revocable) |
| Web sign-in | Own sign-in page with a session cookie; later OIDC/SSO and passkeys |
| Creating users | Admins only; the first admin is created on start from environment variables |
| Snippets | Markdown with alt text, light/dark pair, MDU attributes, plain URL and HTML |
| Image processing | Strip metadata, choose the format right after the upload (WebP), thumbnails, sizes by parameter; with NetVips/libvips |
| File types | Only the MDU image formats: png, jpg/jpeg, gif, webp, avif, bmp, svg |
| Statistics | Counter, last request, referring pages; bots and requests from the management UI are not counted |
| mdu CLI | New command `mdu assets upload`; `mdu lint` checks asset links |
| Deleting | Trash with a 30-day retention period |
| UI technology | Embedded static HTML/CSS + vanilla JavaScript that uses the JSON API |
| Design | Take over `tokens.css` from the MDU design system; a test checks that the copy is current |
| UI language | English, like MDU |
| Organizing and finding | Tags (several per image), search and filters, reverse lookup by URL |
| Limits | Max. file size, storage quota per project, rate limiting, audit log |
| Operations | Docker image `capjan/mdu-assets`, native binaries, own repository, project export/import |
| First version | Contains everything above except the sign-in methods marked "later" |

## 4. Roles and permissions

### 4.1 System roles

| Role | Rights |
| --- | --- |
| Admin | Sees and manages all projects, manages users, quotas, global settings, the audit log and the trash of all projects |
| User | Sees only projects they are a member of; manages their own password and API tokens |

### 4.2 Project roles

| Action | Viewer | Editor | Owner |
| --- | :-: | :-: | :-: |
| View assets, tags and statistics, copy snippets | ✓ | ✓ | ✓ |
| Upload, replace, edit metadata, move, tag | | ✓ | ✓ |
| Assign, rename, merge and delete tags | | ✓ | ✓ |
| Move assets to the trash and restore them | | ✓ | ✓ |
| Add members, change roles, remove members | | | ✓ |
| Rename, export and delete the project | | | ✓ |
| Empty the trash permanently | | | ✓ |

A project always has at least one owner. Admins have owner rights in every project.

## 5. Functional requirements

Priority: **M** = must (first version), **S** = should (first version, may follow later if needed), **L** = later.

### 5.1 Sign-in and users

| ID | Requirement | Prio |
| --- | --- | :-: |
| AUTH-01 | The web UI has its own sign-in page with user name and password; a menu behind the user name holds account, admin and sign-out. Tools sign in to the API with HTTP Basic Auth or an API token; browsers never get the Basic Auth prompt. Passwords are accepted only over HTTPS (directly or behind a reverse proxy with `X-Forwarded-Proto`). | M |
| AUTH-02 | Passwords are stored with PBKDF2 (SHA-256, at least 600,000 iterations, random salt). | M |
| AUTH-03 | On the first start without users, an admin is created from `MDU_ASSETS_ADMIN_USER` and `MDU_ASSETS_ADMIN_PASSWORD`. If the variables are missing, the server stops with a clear error message. | M |
| AUTH-04 | Admins create users with an initial password, lock and unlock them, reset passwords and grant the admin role. The last admin cannot be removed or locked. | M |
| AUTH-05 | Users change their own password; an initial password must be changed at the first sign-in. | S |
| AUTH-06 | Users create personal API tokens with a name, an expiry date (default 90 days, "never" selectable) and an optional restriction to one project and to read or write access. The token is shown only once and stored only as a hash. The list shows name, prefix, expiry and last use. | M |
| AUTH-07 | Tokens are passed as `Authorization: Bearer <token>`. A token has at most the rights of its user. | M |
| AUTH-08 | Failed sign-ins are limited per user and IP (rate limiting, see OPS-05). | M |
| AUTH-09 | Session cookie (`HttpOnly`, `Secure`, `SameSite=Strict`), optionally "stay signed in"; locking, deleting and a password change end the user's sessions immediately; protection against requests from other sites ([ADR-0021](Architecture/Architecture-Decisions/0021-Sign-In-Page-and-Session-Cookie.md)). | M |
| AUTH-10 | Sign-in through OIDC (for example GitHub, Entra ID, Keycloak), mapped to existing users. | L |
| AUTH-11 | Sign-in with passkeys (WebAuthn). | L |

### 5.2 Projects and members

| ID | Requirement | Prio |
| --- | --- | :-: |
| PRJ-01 | Admins and (if the admin allows it) users create projects with name, short name (slug) and description. The creator becomes owner. | M |
| PRJ-02 | The project overview shows each user their projects with the number of assets, used storage and last change; admins see all projects. | M |
| PRJ-03 | Owners manage members and their roles (Viewer, Editor, Owner). | M |
| PRJ-04 | A project can be renamed without changing asset links. | M |
| PRJ-05 | A project is deleted only when it is empty or the owner explicitly confirms deleting all its assets; the assets then go to the trash. | M |

### 5.3 Uploading

| ID | Requirement | Prio |
| --- | --- | :-: |
| UP-01 | Files can be uploaded by drag and drop onto the whole project window. While dragging, a drop zone shows the target project and folder. | M |
| UP-02 | Images from the clipboard can be pasted with ++ctrl+v++ / ++cmd+v++ anywhere in the project view (screenshot workflow). Pasted images without a file name get a name like `screenshot-2026-11-13-1742.png`. | M |
| UP-03 | Several files are uploaded at once, with progress per file. | M |
| UP-04 | Before or right after the upload, the file name and alt text can be set. The alt text is suggested from the file name (`app-settings.png` → "App settings"). | M |
| UP-05 | After the upload, the asset appears immediately with its Markdown snippet and a highlighted copy button. Where the browser allows it, the snippet is copied to the clipboard directly, and a notice confirms it. For several files there is "Copy all snippets". | M |
| UP-06 | Only png, jpg/jpeg, gif, webp, avif, bmp and svg are allowed. The type is checked by the file content (signature), not only by the extension. | M |
| UP-07 | SVG files are sanitized on upload (no scripts, event handlers, external references or `foreignObject`); the sanitizer follows the allowlist of MDU's SVG reader but is a separate sanitizer for complete files ([ADR-0012](Architecture/Architecture-Decisions/0012-Own-SVG-Sanitizer.md)). | M |
| UP-08 | For raster images, EXIF, GPS and other metadata are removed; the color profile is kept and the orientation is applied first. | M |
| UP-09 | When someone uploads a file whose content already exists in the project (SHA-256), the UI points this out and offers the existing link. | S |
| UP-10 | Uploads above the size limit or quota are rejected with an understandable message before the file has been transferred completely (when `Content-Length` is known). | M |
| UP-11 | Right after the upload, the server chooses the format with the best ratio of size and quality and serves only that format from then on: photos (JPEG) become lossy WebP in high quality; PNG, BMP and still GIF become lossless WebP, unless the content is photographic (lossless more than twice the size of lossy), then lossy. Each only if the file gets smaller. On upload and replacement, "Lossless" can be chosen (`?lossless=true`): then no quality is lost, and JPEG stays unchanged. SVG, AVIF, WebP and animations stay as they are. File name and extension follow the stored format. Only the converted file is stored; type and size of the upload are shown. Can be turned off per project; existing images stay unchanged ([ADR-0019](Architecture/Architecture-Decisions/0019-Format-Chosen-on-Upload.md)). | M |

### 5.4 Links and snippets

| ID | Requirement | Prio |
| --- | --- | :-: |
| LNK-01 | Every asset has a permanent link `/i/<asset-id>/<file-name>`. Tags, renaming projects and new versions do not change it. | M |
| LNK-02 | `/i/<asset-id>` and `/i/<asset-id>/<any-name>` return the same file. After a rename, old links keep working. | M |
| LNK-03 | Asset IDs are at least 10 Base62 characters from a cryptographic random number generator and not sequential. | M |
| LNK-04 | The public base URL is configurable and may be a different host than the management UI (recommended, for example `img.example.com` and `assets.example.com`), so that served files have no access to UI cookies. | M |
| LNK-05 | Snippet "Markdown": `![<alt>](<link>)`. | M |
| LNK-06 | Snippet "Light/dark": two assets can be linked as a pair; the snippet contains both lines with `#only-light` and `#only-dark` and the same alt text. When two files with suffixes such as `-light`/`-dark` are uploaded, the UI suggests the pair. | M |
| LNK-07 | Switches for MDU attributes appended to the snippet: `{.image-frame}`, `{.image-zoom-on}`, `{.image-zoom-off}`, caption and width (`?w=`). The UI remembers the last chosen switches per user. | M |
| LNK-08 | Snippets "URL" and "HTML" (`<img src alt width height>`). | M |
| LNK-09 | The reverse lookup takes an asset URL (also with a size parameter or fragment) and opens the asset if the user has access. | M |

### 5.5 Delivery and image processing

| ID | Requirement | Prio |
| --- | --- | :-: |
| DLV-01 | Public links return the current version without sign-in, with the correct `Content-Type`, `ETag`, `Last-Modified` and support for `If-None-Match`/`If-Modified-Since`. | M |
| DLV-02 | Because the content behind a link can change, links use `Cache-Control: public, max-age=300, stale-while-revalidate=86400` (configurable). In addition there are version-pinned links `/i/<asset-id>/v<n>/<file-name>` with `immutable`. | M |
| DLV-03 | `?w=<width>` returns a proportionally reduced variant (never enlarged). Only configured widths are allowed (default: 320, 640, 800, 1024, 1280, 1600, 2048), so attackers cannot create an unbounded cache. Derived variants are cached on disk. | M |
| DLV-04 | A link always returns the stored format (UP-11), regardless of the `Accept` header; `?w=` resizes in the same format. That way extension and `Content-Type` match. Concurrent image jobs are limited. | M |
| DLV-05 | SVG is served with `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox`; all responses carry `X-Content-Type-Options: nosniff`. | M |
| DLV-06 | Thumbnails for the UI are created on upload and served through a signed-in endpoint that is not counted. | M |
| DLV-07 | Assets in the trash return `410 Gone` (optionally a configurable placeholder image); unknown IDs return `404`. | M |
| DLV-08 | Delivery supports `HEAD` and `Range` requests. | S |

### 5.6 Organizing, searching and views

| ID | Requirement | Prio |
| --- | --- | :-: |
| ORG-01 | *Dropped:* nested folders. Tags replace them; on update, the folder of an image and all folders above it become tags ([ADR-0020](Architecture/Architecture-Decisions/0020-Tags-Instead-of-Folders.md)). | – |
| ORG-02 | The sidebar shows the tags of the project with their counts; a click filters by a tag, and more tags narrow the filter further. Images are tagged one at a time or as a multiple selection, by drag and drop onto a tag or through "Tags…". | M |
| ORG-03 | Free tags per asset, with suggestions from the tags that exist in the project. Several images can be tagged at once (selection or dragging onto a tag in the sidebar); tags can be renamed, merged and deleted across the whole project. Uploads get the tags currently filtered by. | M |
| ORG-04 | Search across file name, alt text, description and tags, already while typing; filters by one or more tags (all must match) or "without tags", file type, upload period, uploader and "never requested" / "not requested for N days". | M |
| ORG-05 | Project view as a tile grid (preview, name, size, requests) and as a list with sortable columns (name, size, dimensions, upload, last request, requests). | M |
| ORG-06 | Detail view: large preview (light/dark background switchable, opens by click or keyboard in MDU's image viewer with zoom and panning), all snippets, metadata (type, dimensions, size, hash, uploader, date), versions, statistics, referring pages, light/dark partner. | M |
| ORG-07 | Keyboard operation: arrow keys in the grid and the list, also without focusing an image first and with ++down++ from the search field, ++enter++ opens the details, ++c++ copies the Markdown snippet of the selected asset, ++delete++ moves it to the trash. | S |

### 5.7 Versions and trash

| ID | Requirement | Prio |
| --- | --- | :-: |
| VER-01 | Editors replace an asset with a new file (drag and drop onto the detail view, paste or file picker). The link stays, the version number increases. | M |
| VER-02 | A replacement may change the format (for example png → webp, also through UP-11). The file name then gets the new extension; links with the old name keep working because only the ID counts, and version links of older versions keep their extension. The UI shows a notice. | M |
| VER-03 | The version list shows date, user, size and dimensions; every version can be retrieved and made current again (as a new version). | M |
| VER-04 | Old versions count toward the quota. Owners can permanently delete individual old versions. Optional: automatic cleanup after N versions (project setting). | S |
| DEL-01 | Deleting moves assets to the trash. If the asset was requested in the last 30 days, the UI warns first with the number of requests and the referring pages. | M |
| DEL-02 | Assets in the trash can be restored for 30 days (configurable), with the same link, and are then permanently deleted automatically. | M |
| DEL-03 | Owners and admins can empty the trash early; permanent deletion requires a confirmation. | M |

### 5.8 Request statistics

| ID | Requirement | Prio |
| --- | --- | :-: |
| STA-01 | For each asset, the total number of requests and the time of the last request are stored. | M |
| STA-02 | Successful public `GET` responses (200 and 304) are counted, regardless of size parameter or format. `HEAD`, UI thumbnails and requests from signed-in UI sessions are not counted. | M |
| STA-03 | Known bots and crawlers (user agent list, configurable) are not counted. | M |
| STA-04 | Referring pages: from the `Referer`, origin and path (without query and fragment) are stored with a count and the last time, at most 100 entries per asset (the oldest drop out). | M |
| STA-05 | Counters are collected in memory and written to SQLite in batches (for example every 10 s), so requests do not wait for database writes. | M |
| STA-06 | No IP addresses or user agents per request are stored permanently. | M |

**Known limits** (explained in the UI): browser and CDN caches reduce the number of requests that reach the server. By default, browsers send only the origin as the referrer for requests from other sites (`strict-origin-when-cross-origin`); referring pages are therefore mostly sites, not individual pages. See open question Q-03.

### 5.9 API

| ID | Requirement | Prio |
| --- | --- | :-: |
| API-01 | JSON API under `/api/v1`, signed in through the web UI session, Basic Auth or an API token. Every UI function is available through the API; the UI uses this API exclusively. | M |
| API-02 | The API is described as an OpenAPI document at `/api/v1/openapi.json` that MDU can render as an API reference. | M |
| API-03 | Errors are reported uniformly as `application/problem+json` (RFC 9457). | M |
| API-04 | The upload endpoint accepts `multipart/form-data` and the raw content in the body (`Content-Type: image/png`, file name through `?name=`), so simple tools such as `curl --data-binary` or ShareX work too. | M |
| API-05 | The upload response contains at least `id`, `url`, `versionUrl`, `markdown`, `html`, `width`, `height`, `size`, `contentType`. | M |
| API-06 | A public endpoint without sign-in answers for a list of asset URLs whether they exist, are in the trash or are unknown (for `mdu lint`). It reveals no metadata besides the status. | M |
| API-07 | Under "Integrations", the UI offers a ready-made ShareX configuration (`.sxcu`) and a `curl` example for the chosen project and token for download. | S |

Endpoint sketch:

| Method and path | Purpose |
| --- | --- |
| `POST /api/v1/projects/{project}/assets` | Upload (optional `alt`, `tag`, repeatable) |
| `GET /api/v1/projects/{project}/assets` | List with search, filters, sorting, paging |
| `GET /api/v1/assets/{id}` | Metadata, snippets, short statistics |
| `PATCH /api/v1/assets/{id}` | Name, alt text, description, tags, light/dark partner |
| `PUT /api/v1/assets/{id}/content` | Upload a new version |
| `GET /api/v1/assets/{id}/versions` · `POST …/versions/{n}/restore` | Versions |
| `GET /api/v1/assets/{id}/stats` | Counter, last request, referring pages |
| `DELETE /api/v1/assets/{id}` · `POST /api/v1/assets/{id}/restore` | Trash |
| `POST /api/v1/assets/lookup` | Reverse lookup (signed in) |
| `POST /api/v1/public/check` | Existence check for lint (public) |
| `…/projects`, `…/projects/{project}/tags`, `…/projects/{project}/assets/tags`, `…/projects/{project}/members` | Projects, tags, members |
| `…/me`, `…/me/tokens` | Own profile and tokens |
| `…/admin/users`, `…/admin/audit`, `…/admin/settings` | Administration |
| `GET /api/v1/projects/{project}/export` · `POST /api/v1/projects/import` | Export/import |

### 5.10 Integration with the mdu CLI

These requirements concern the `capjan/mdu` repository.

| ID | Requirement | Prio |
| --- | --- | :-: |
| CLI-01 | `mdu.yml` gets an `assets` section with `server` (base URL of the API), `public` (public base URL, if different) and `project` (default project). The token comes from the environment variable `MDU_ASSETS_TOKEN`, never from `mdu.yml`. | M |
| CLI-02 | `mdu assets upload <file>… [--project] [--tag] [--alt] [--format markdown\|url\|html]` uploads files and prints the snippet for each file as plain text, so it can be piped. With `-`, the command reads from stdin. | M |
| CLI-03 | `mdu lint` reports image and link targets on the configured asset server that are unknown or in the trash, as the new rule `MDU032`. The check uses the batched check endpoint (API-06), runs only when `assets.server` is configured, and reports network errors as warnings, not errors. `--offline` skips it. | M |
| CLI-04 | The CLI output stays plain text and dependency-light; HTTP access uses `HttpClient` without additional packages. | M |

### 5.11 Administration, limits and logging

| ID | Requirement | Prio |
| --- | --- | :-: |
| OPS-01 | Max. file size per upload: global (default 50 MB) and lower per project if set. Max. pixel count (default 50 MP) against decompression bombs. | M |
| OPS-02 | Storage quota per project (current versions + old versions + trash), set by the admin; the UI shows the usage and warns from 90 %. Cached size and format variants do not count. | M |
| OPS-03 | Audit log for sign-ins (successful/failed), uploads, new versions, moves, renames, metadata changes, deletions, restores, and member, token and user changes. Admins see everything, owners the log of their project. Retention configurable (default 365 days). | M |
| OPS-04 | Admin overview: users, projects with usage, total storage, trash, settings. | M |
| OPS-05 | Rate limiting (ASP.NET Core middleware) per IP for sign-ins, uploads, the public check endpoint and 404 responses under `/i/`. | M |
| OPS-06 | Export of a project as a zip (current files, optionally all versions, metadata as JSON); importing into another instance keeps the asset IDs if they are free. | M |
| OPS-07 | Health endpoint `/healthz` and structured logs on stdout. | M |

### 5.12 Interface and design

| ID | Requirement | Prio |
| --- | --- | :-: |
| UI-01 | The UI uses the tokens from `tokens.css` of the MDU design system unchanged (colors, typography, spacing, radii, shadows) and builds components to its guidelines: header, sidebar with tree, controls, cards/tiles as in the dashboard, callouts for notices. | M |
| UI-02 | Light, dark and automatic mode as in MDU, with the same switch. | M |
| UI-03 | Keyboard focus as in MDU: only `:focus-visible`, `outline: 2px solid var(--link); outline-offset: 3px`, rounding and inset ring following the MDU rules. | M |
| UI-04 | Accessibility following the "Accessibility" chapter of the design system: contrast, reduced motion, full keyboard operation, live region for upload status and copy confirmation. | M |
| UI-05 | Responsive layout from 360 px width; uploading through the file picker on mobile devices too. | S |
| UI-06 | All UI files are embedded in the application; no CDNs, no external fonts. | M |
| UI-07 | A test in the repository checks that `tokens.css` matches the version in the design system (for example through a hash in a pin file), and a tool updates the copy. | M |

Pages: sign-in (later), projects, project (tree + grid/list + upload area), asset details, trash, project settings (members, limits, format conversion), profile (password, tokens, integrations), admin (users, projects, audit log, settings).

## 6. Non-functional requirements

| ID | Requirement |
| --- | --- |
| NF-01 | .NET 10, ASP.NET Core minimal APIs with the Request Delegate Generator, `PublishAot=true`, without trim or AOT warnings. No Razor Pages, no MVC, no reflection-based JSON (System.Text.Json source generator). |
| NF-02 | Data access with `Microsoft.Data.Sqlite` and handwritten SQL (no EF Core), schema migrations versioned in code. SQLite in WAL mode. |
| NF-03 | Image processing with NetVips/libvips (NetVips 3.2.0, NetVips.Native 8.18.7 or newer). The [AOT spike](Architecture/NetVips-AOT-Spike.md) confirmed that it works under Native AOT (see R-01). |
| NF-04 | Only .NET as build, test and generator tooling, no Node/npm and no Python – as in the MDU repository. Tests with xUnit. |
| NF-05 | Files are stored under a content-addressed path (`blobs/ab/cd/<sha256>`), and metadata refers to it. Writes are atomic (temporary file + rename). |
| NF-06 | Backup: copying the data directory after `sqlite3 .backup` or a built-in command `mdu-assets backup <target>` produces a consistent backup. |
| NF-07 | Performance: served originals and cached variants with p95 < 20 ms (without network) on a small VPS; cold start < 200 ms; idle memory < 80 MB. |
| NF-08 | Configuration through `appsettings.json` and environment variables (`MDU_ASSETS_…`), among them data directory, public URL, UI URL, limits and trusted proxies. |
| NF-09 | Privacy: no cookies on public links, no storage of personal request data (STA-06), metadata removed from images (UP-08). |
| NF-10 | Repository conventions as in MDU: Conventional Commits, git-cliff for versions and changelog, release workflow from `main`. |

## 7. Operations and delivery

| ID | Requirement | Prio |
| --- | --- | :-: |
| DEP-01 | Own repository `mdu-assets` (locally `/Volumes/SSK/Code/mdu-assets`). | M |
| DEP-02 | Multi-architecture Docker image `capjan/mdu-assets` (linux/amd64, linux/arm64) with libvips, running as a non-root user, one volume `/data`. | M |
| DEP-03 | Native binaries for linux-x64, linux-arm64 and osx-arm64 as release assets (Windows is currently not a target); libvips (17–18 MB) ships next to the program as a native library (archive instead of a single file), together with the license texts. On macOS, the program and `libvips.42.dylib` are signed and notarized as in MDU. | M |
| DEP-04 | Example configuration for running behind a reverse proxy (Caddy or nginx) with separate hosts for the UI and public links. | S |

## 8. Data model (sketch)

```text
User(id, name, display_name, password_hash, is_admin, is_locked, must_change_password, created_at)
ApiToken(id, user_id, name, prefix, hash, project_id?, scope, expires_at?, last_used_at?, created_at)
Project(id, slug, name, description, quota_bytes?, max_file_bytes?, convert_formats, created_at)
Membership(project_id, user_id, role)
Asset(id /*public base62*/, project_id, file_name, alt_text, description,
      pair_asset_id?, pair_role?, current_version, deleted_at?, created_by, created_at)
AssetVersion(asset_id, number, blob_sha256, content_type, size, width, height, created_by, created_at)
Tag(asset_id, name)
AssetStats(asset_id, hit_count, last_hit_at)
AssetReferrer(asset_id, origin, path, hit_count, last_hit_at)
AuditEntry(id, at, user_id?, project_id?, action, target, details_json, ip)
```

## 9. Acceptance criteria for the core workflows

1. **Paste a screenshot:** signed in to a project, paste a screenshot with ++cmd+v++ → within 2 s a Markdown snippet with a link of the form `/i/<id>/screenshot-….png` is ready and can be copied with one click or ++c++. The link shows the image in a new browser session that is not signed in.
2. **Reorganize:** give the asset other tags and rename a tag → the copied link still returns the same image.
3. **Replace:** drag a new file onto the detail view → after the cache time the link shows the new image; version 1 stays available under `/v1/` and can be restored.
4. **Collaboration:** an owner adds a second user as editor → that user sees the project and can upload but cannot manage members; a viewer sees no upload area and gets `403` for API uploads.
5. **Admin:** an admin sees all projects, even without membership, and can create and lock users; a locked user and their tokens are rejected immediately.
6. **API:** `curl -H "Authorization: Bearer …" --data-binary @image.png -H "Content-Type: image/png" "…/api/v1/projects/docs/assets?name=image.png"` returns `201` with `url` and `markdown`; `GET /api/v1/assets/{id}` returns metadata and statistics.
7. **Statistics:** three requests from a normal browser increase the counter by 3 (or by the number of requests not served from the cache) and set the last request; requests with a Googlebot user agent and views in the UI do not change the counter.
8. **mdu:** `mdu assets upload image.png` prints a Markdown line; after the asset is deleted, `mdu lint` reports the link as `MDU032`.
9. **Design:** light and dark mode match the tokens of the design system; tabbing through the header, tree, grid and detail view shows the MDU focus ring everywhere.

## 10. Stages

| Stage | Content |
| --- | --- |
| 1 (first version) | All requirements with priority M and S |
| 2 | Sign-in form with session (AUTH-09), passkeys (AUTH-11) |
| 3 | OIDC/SSO (AUTH-10) |
| Ideas, not decided | Invitation links; S3-compatible storage; paste-to-asset in the `mdu serve` editor; a command that copies asset links into local files (offline/PDF) or uploads local images and replaces the links; image editing in the UI (cropping, annotations, redaction); request history per day as a chart; webhooks on uploads; more file types (PDF, video) |

## 11. Risks

| ID | Risk | Handling |
| --- | --- | --- |
| R-01 | NetVips under Native AOT and AVIF encoding in the bundled libvips. | **Mitigated by the [spike](Architecture/NetVips-AOT-Spike.md):** 0 AOT warnings, all checks passed on osx-arm64 and Linux. Note: build multi-architecture images natively or by cross-compiling, not under emulation. |
| R-02 | Native binaries are not a single file because of libvips. | Confirmed. Release as an archive; Docker (chiseled `runtime-deps` is enough) as the recommended way. |
| R-07 | AVIF encoding is slow (≈ 1 s for 1600×900) and memory-hungry. | The server no longer creates AVIF but WebP on upload (≈ 0.2 s, UP-11); limited concurrency, libvips cache limited. |
| R-03 | Public images with unguessable IDs are no access control; links once shared cannot be taken back. | State this clearly in the UI and documentation; deleting returns 410 immediately. |
| R-04 | SVG uploads are an XSS vector. | Sanitizing (UP-07), CSP with `sandbox` (DLV-05), separate host (LNK-04). |
| R-05 | Request counts are systematically too low because of caches and CDNs. | Short `max-age` (DLV-02), explain the limit in the UI. |
| R-06 | Basic Auth does not allow a clean sign-out in the browser. | Done: sign-in page with a session ([ADR-0021](Architecture/Architecture-Decisions/0021-Sign-In-Page-and-Session-Cookie.md)). |

## 12. Open questions

| ID | Question |
| --- | --- |
| Q-01 | May regular users create projects themselves, or only admins? (PRJ-01 provides an admin setting.) **Decided:** admins always, other users only with the admin setting, which is off at first ([ADR-0014](Architecture/Architecture-Decisions/0014-Project-Creation-by-Admin-Setting.md)). |
| Q-02 | Should a variant converted by size parameter or format be discarded immediately on replacement (yes, planned) – and should the server offer a purge hook when running behind a CDN? **Decided:** variants stay for the version-pinned links, no purge hook ([ADR-0016](Architecture/Architecture-Decisions/0016-Version-Retention-and-Cache-Purging.md)). |
| Q-03 | Should MDU set `referrerpolicy="no-referrer-when-downgrade"` on images from the asset server when rendering, so the server sees individual pages instead of only sites? This is a privacy trade-off for readers. **Decided:** no, MDU keeps the browser default ([ADR-0017](Architecture/Architecture-Decisions/0017-Referrer-Policy-for-MDU-Pages.md)). |
| Q-04 | How many versions per asset should be kept by default (unlimited vs. for example 20)? **Decided:** unlimited; owners can set a limit per project ([ADR-0016](Architecture/Architecture-Decisions/0016-Version-Retention-and-Cache-Purging.md)). |
| Q-05 | May asset IDs collide on import into another instance (then new IDs and a mapping file), or does the import fail? **Decided:** new IDs for taken ones, with the mapping in the response ([ADR-0018](Architecture/Architecture-Decisions/0018-Import-Id-Collisions.md)). |
| Q-06 | ~~Which host name is intended?~~ Decided: the host names for the interface and public links are configuration per installation (LNK-04, NF-08); there is no fixed host name. |
