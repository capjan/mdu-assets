---
navigation.order: 60
description: The JSON API of the MDU Asset Server for scripts and tools, with sign-in, uploads, errors and the OpenAPI description.
---

# API

Everything the web interface does goes through a JSON API under `/api/v1` on the UI host. The full description is an OpenAPI document at:

```text
https://assets.example.com/api/v1/openapi.json
```

It needs no sign-in, so tools and MDU's `openapi` code block can read it directly.

## Sign-in

| Way | For |
| --- | --- |
| `Authorization: Bearer mdua_…` | Tools and scripts. Create the token on the [Account](Usage/Account.md#api-tokens) page; it can be limited to one project and to reading |
| HTTP Basic with user name and password | Quick tests with curl. Browsers never get a Basic prompt, and Basic credentials sent by a browser are ignored |
| Session cookie | The web interface itself |

Passwords and tokens are only accepted over HTTPS. Requests from scripts in other sites' pages are refused by their `Origin` and `Sec-Fetch-Site` headers.

## Upload

Send the raw file in the body with its type and the name in the query:

```bash
curl -H "Authorization: Bearer mdua_…" -H "Content-Type: image/png" --data-binary @screenshot.png \
  "https://assets.example.com/api/v1/projects/docs/assets?name=screenshot.png&alt=Settings%20page&tag=setup"
```

Or as `multipart/form-data` with the file in the field `file` and the alt text in `alt`, as ShareX does.

| Query parameter | Meaning |
| --- | --- |
| `name` | File name; the extension follows the stored format |
| `alt` | Alt text; suggested from the file name when empty |
| `tag` | A tag; repeat it for several |
| `lossless=true` | Keep every pixel ([Uploading](Usage/Uploading.md#the-format-is-chosen-on-upload)) |
| `duplicates=reject` | Answer `409` with the existing image instead of storing the same content twice |

The answer `201 Created` holds the image:

```json
{
  "id": "k3Fq9xZ2aB",
  "url": "https://img.example.com/i/k3Fq9xZ2aB/screenshot.webp",
  "versionUrl": "https://img.example.com/i/k3Fq9xZ2aB/v1/screenshot.webp",
  "markdown": "![Settings page](https://img.example.com/i/k3Fq9xZ2aB/screenshot.webp)",
  "html": "<img src=\"https://img.example.com/i/k3Fq9xZ2aB/screenshot.webp\" alt=\"Settings page\" width=\"1280\" height=\"800\">",
  "width": 1280,
  "height": 800,
  "size": 48213,
  "contentType": "image/webp",
  "tags": ["setup"]
}
```

The real answer has more fields, such as `fileName`, `altText`, `version`, `sha256`, `requests` and the type and size of the original upload. With `duplicates=reject`, a repeated upload answers `409` with `title`, `detail` and the image in `existing`.

## Check links

`POST /api/v1/public/check` tells for up to 100 public links whether they still work. It needs no sign-in and reveals nothing but the status; `mdu lint` uses it.

```bash
curl -H "Content-Type: application/json" \
  -d '{"urls":["https://img.example.com/i/k3Fq9xZ2aB/screenshot.webp"]}' \
  https://assets.example.com/api/v1/public/check
```

```json
{ "results": [ { "url": "https://img.example.com/i/k3Fq9xZ2aB/screenshot.webp", "status": "ok" } ] }
```

| Status | Meaning |
| --- | --- |
| `ok` | The image (or the version in a version link) exists |
| `trash` | The image is in the trash |
| `deleted` | The image was deleted for good |
| `unknown` | No such image or version |
| `invalid` | Not a link to this server |

## Errors

Errors are `application/problem+json` ([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)) with a `title` and a `detail` meant for people, for example:

```json
{ "status": 413, "title": "File too large", "detail": "Uploads may have at most 10,485,760 bytes." }
```

`401` means the token or password is missing or wrong, `403` that the user lacks the role, `404` that the project or image does not exist or is not visible to the user, and `429` that a rate limit was reached.

## Endpoints

| Method and path | Purpose |
| --- | --- |
| `GET /projects` · `POST /projects` | List your projects, create one |
| `GET`, `PATCH`, `DELETE /projects/{project}` | A project and its settings |
| `GET`, `PUT`, `DELETE /projects/{project}/members/…` | Members and roles |
| `GET /projects/{project}/assets` | Images with `q`, `tag` (repeatable), `untagged`, `type`, `from`, `to`, `uploader`, `unused`, `limit` and `after` for paging |
| `POST /projects/{project}/assets` | Upload |
| `POST /projects/{project}/assets/tags` | Add or remove tags on many images |
| `PATCH`, `DELETE /projects/{project}/tags` | Rename, merge or delete a tag in the whole project |
| `GET /projects/{project}/facets` | Tags, types and uploaders with their counts |
| `GET`, `PATCH`, `DELETE /assets/{id}` | One image: read, change name, alt text, description and tags, move to the trash |
| `POST /assets/{id}/restore` | Restore from the trash |
| `GET`, `POST /assets/{id}/versions` | Versions; upload a new one |
| `POST /assets/{id}/versions/{n}/restore` · `DELETE /assets/{id}/versions/{n}` | Make a version current again, delete an old one |
| `PUT`, `DELETE /assets/{id}/pair` | Light and dark partner |
| `GET /assets/{id}/referrers` | Referring pages |
| `GET /lookup?url=…` | The image behind a public link |
| `GET`, `DELETE /projects/{project}/trash…` | The trash of a project |
| `GET /projects/{project}/export` · `POST /projects/import` | Move a project |
| `GET /me` · `POST /me/password` · `GET`, `POST`, `DELETE /me/tokens…` | Your account and tokens |
| `/users…`, `/settings`, `/admin/overview`, `/admin/audit`, `/projects/{project}/audit` | Administration and audit log |

All paths are relative to `/api/v1`. The OpenAPI document lists the parameters and answers of each.
