---
description: Uploads are re-encoded only to remove metadata, ?w= snaps to configured widths, and WebP or AVIF copies are served only when smaller.
---

# 0015 Image Variant Rules

- Status: Accepted; the format rules are superseded by [0019](0019-Format-Chosen-on-Upload.md)
- Date: 2026-11-15

## Context

M5 adds image processing (UP-08, DLV-03, DLV-04, DLV-06). Every re-encoding of a lossy image loses quality, every new width adds a file to the cache, and conversion costs time: WebP takes about a fifth of a second, AVIF about a second (see the [NetVips AOT spike](../NetVips-AOT-Spike.md)). DLV-03 allows only configured widths, so that nobody can fill the cache with arbitrary sizes, but documentation that asks for `?w=700` should still show an image.

## Options

- Re-encode every upload, or only uploads that carry metadata or an orientation
- Reject widths that are not configured, or snap them to the next configured width
- Always serve WebP or AVIF when the browser accepts them, or only when the copy is smaller

## Decision

- **Uploads:** an upload is re-encoded only when it carries EXIF, XMP, IPTC or PNG comments or needs rotating. Then the orientation is applied, the metadata removed and the ICC profile kept, in the original format (JPEG quality 92, WebP 90, PNG lossless). Everything else, including most screenshots, keeps its exact bytes. GIF and BMP carry no such metadata.
- **Widths:** `?w=` snaps to the smallest configured width that is not smaller than the request (default 320, 640, 800, 1024, 1280, 1600, 2048; `MDU_ASSETS_WIDTHS`). Larger requests, and widths at or above the original, serve the original. Resized copies keep the format of the original; BMP becomes PNG.
- **Formats:** for PNG, JPEG, BMP and still WebP, a browser that accepts AVIF gets the AVIF copy once the background queue has made it ([ADR-0011](0011-AVIF-in-the-Background.md)). A browser that accepts WebP gets a WebP copy, which is made on the first request. A copy that is not smaller than its source is remembered as such and never served. SVG, GIF, animated WebP and AVIF originals keep their format. Owners can turn conversion off per project.
- **Previews:** previews for the web interface are WebP, at most 480 pixels on the longer side, made on upload and served only to signed-in members of the project.

## Consequences

- Most uploads never lose quality, and the stored file is the uploaded file whenever nothing had to be removed.
- The variant cache is bounded by the number of configured widths times three formats per image.
- Answers depend on `Accept`, so every convertible image is served with `Vary: Accept`; ETags differ per width and format.
- Deleting `variants/` only costs the time to create the copies again.
