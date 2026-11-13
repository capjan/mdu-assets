---
description: The upload reads the image type and size from the file header with own code, without an image library.
---

# 0013 Image Headers Without an Image Library

- Status: Accepted
- Date: 2026-11-13

## Context

An upload must be recognized by its content (UP-06) and rejected when it has too many pixels before anything decodes it (OPS-01). The API also returns width and height (API-05). NetVips can read headers, but libvips has no BMP loader in NetVips.Native, and the server does not need image processing before M5.

## Options

- Read headers with NetVips
- Parse the headers of the seven formats with own code

## Decision

`ImageDetector` reads the signature and the size from the header of PNG, JPEG, GIF, WebP (VP8, VP8L, VP8X), AVIF (`ftyp` brand and `ispe` property) and BMP. SVG is recognized by parsing it as XML in the sanitizer, with the size from `width`/`height` or the `viewBox`. HEIC and other HEIF files that browsers cannot show are rejected.

## Consequences

- Detection works without native code, so the container needs no libvips until image processing starts in M5.
- The pixel limit applies before any decoder sees the file.
- The detector only reads headers; a file with a valid header and broken data is accepted. Processing in M5 will notice such files.
