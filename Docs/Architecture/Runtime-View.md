---
navigation.order: 6
description: How the MDU Asset Server handles uploads, public requests, replacements and background work.
---

# Runtime View

## Paste a screenshot and copy the snippet

```mermaid
sequenceDiagram
    accTitle: Paste a screenshot and copy the snippet
    actor Author
    participant UI as Web interface
    participant API
    participant Img as Image pipeline
    participant Store as Storage
    participant Jobs as Background jobs
    Author->>UI: Cmd+V in the project view
    UI->>+API: POST /api/v1/projects/docs/assets (image/png)
    API->>API: Check role, size limit, quota
    API->>Img: Detect type, strip metadata, read size
    Img-->>API: Cleaned bytes, 1600x900
    API->>Img: Encode as WebP
    Img-->>API: Smaller lossless WebP
    API->>Store: Write WebP and original blobs, asset, version 1, audit entry
    API->>Img: Create thumbnail
    API-->>-UI: 201 id, url, markdown
    UI->>UI: Copy Markdown to the clipboard
    UI-->>Author: Snippet shown and copied
```

The upload answers as soon as the image in its chosen format, the kept original and the thumbnail are stored; the snippet already carries the `.webp` name ([ADR-0019](Architecture-Decisions/0019-Format-Chosen-on-Upload.md)).

> [!NOTE]
> Since M9 the sequence runs as shown.

## First sign-in with a start password

```mermaid
sequenceDiagram
    accTitle: First sign-in with a start password
    actor Admin
    actor User
    participant UI as Web interface
    participant API
    Admin->>API: POST /api/v1/users {name}
    API-->>Admin: 201 start password (shown once)
    Admin-->>User: Hands over the start password
    User->>UI: Opens the web interface, signs in
    UI->>API: GET /api/v1/me
    API-->>UI: mustChangePassword: true
    UI-->>User: Account page only
    User->>UI: Current and new password
    UI->>API: POST /api/v1/me/password
    API-->>UI: 204
    UI-->>User: Browser asks for the new password on the next request
```

Until the password is changed, every other API request answers 403, so a tool cannot work around the account page.

## Serve a public link

```mermaid
sequenceDiagram
    accTitle: Serve a public link
    participant Reader as Reader's browser
    participant Del as Public delivery
    participant Store as Storage
    participant Stats as Usage statistics
    Reader->>+Del: GET /i/k3F9xQ/architecture.png?w=800 (Accept: image/avif)
    Del->>Store: Look up asset k3F9xQ, current version
    alt unknown
        Del-->>Reader: 404
    else in trash
        Del-->>Reader: 410
    else found
        Del->>Store: Variant 800 px, best format available
        alt If-None-Match matches
            Del-->>Reader: 304
        else
            Del-->>-Reader: 200 image bytes, ETag, Cache-Control, Vary: Accept
        end
        Del-)Stats: Count request, referrer (unless bot)
    end
```

The file name in the URL is ignored for the lookup, so old links still work after a rename. Statistics are recorded after the response is on its way and written to SQLite in batches.

## Replace an image

1. An editor drops a new file on the asset details.
2. The image pipeline cleans it as on upload; storage writes a new blob and version 2 and points the asset at it.
3. Variants of version 1 stay for its version link ([ADR-0016](Architecture-Decisions/0016-Version-Retention-and-Cache-Purging.md)); version 2 is converted like an upload, gets its own preview at once and widths on demand; if its format differs, the asset gets the new extension. With a version limit set for the project, versions beyond it are deleted for good, and blobs no version uses any more go with their variants.
4. The public link serves version 2 once the readers' caches expire (`max-age=300`). Version 1 stays reachable under `/i/<id>/v1/<name>` and can be restored as version 3.

## Background work

```mermaid
stateDiagram-v2
    accTitle: Lifecycle of a variant
    [*] --> Running: upload or first request
    Running --> Ready: encoded and stored
    Running --> Failed: error, the original is served
    Failed --> Running: next request
    Ready --> Dropped: version or blob deleted
    Dropped --> [*]
```

Background services delete trashed assets after the retention period, clean up old audit entries and write request statistics. Image work runs in the request that needs it, bounded by `ImageJobs`; missing variants are made again when requested.
