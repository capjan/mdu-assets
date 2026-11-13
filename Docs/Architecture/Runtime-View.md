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
    API->>Store: Write blob, asset, version 1, audit entry
    API->>Img: Create thumbnail
    API->>Jobs: Queue AVIF variant
    API-->>-UI: 201 id, url, markdown
    UI->>UI: Copy Markdown to the clipboard
    UI-->>Author: Snippet shown and copied
```

The upload answers as soon as the original, its metadata and the thumbnail are stored. The AVIF variant follows in the background ([ADR-0011](Architecture-Decisions/0011-AVIF-in-the-Background.md)).

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
3. Cached variants of version 1 are dropped; new variants are created on demand or in the background.
4. The public link serves version 2 once the readers' caches expire (`max-age=300`). Version 1 stays reachable under `/i/<id>/v1/<name>` and can be restored as version 3.

## Background work

```mermaid
stateDiagram-v2
    accTitle: Lifecycle of a converted variant
    [*] --> Queued: upload or first request
    Queued --> Running: worker free
    Running --> Ready: encoded and stored
    Running --> Failed: error
    Failed --> Queued: retry with backoff
    Ready --> Dropped: new version or deletion
    Dropped --> [*]
```

A bounded in-process queue with a fixed number of workers runs AVIF encoding, the final deletion of trashed assets after the retention period and the cleanup of old audit entries. On restart, missing variants are queued again when requested.
