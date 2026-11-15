---
description: The server picks the format to serve right after upload and keeps the uploaded file, instead of switching formats per browser.
---

# 0019 Format Chosen on Upload

- Status: Accepted
- Date: 2026-11-15

## Context

Until now the server stored the upload and, per request, served a WebP or AVIF copy to browsers that accepted it ([ADR-0011](0011-AVIF-in-the-Background.md), [ADR-0015](0015-Image-Variant-Rules.md)). The link kept the extension of the upload, so `screenshot.png` often delivered WebP, and every answer carried `Vary: Accept`. Replacing an image with another format left the old extension in the link, too. Authors want one file behind a link, in the format that is the best trade-off between size and quality, with the matching extension, decided once.

## Options

- Keep negotiating by `Accept` per request, and only fix the extension on new versions
- Choose the format right after upload; keep the upload or discard it
- Convert to WebP only, or also to AVIF when that is smaller

## Decision

- **On upload and new versions** (UP-11), when the project allows conversion (on by default), the server encodes the cleaned image as WebP and keeps that only if it is smaller:
  - JPEG (photos): lossy WebP, quality 82, with smart chroma subsampling.
  - PNG, BMP and still GIF (screenshots, diagrams, text): lossless WebP, so text stays sharp.
  - SVG, AVIF, WebP, animations, images with more than 8 bits per channel and images larger than 16383 pixels keep their format.
- **WebP, not AVIF:** WebP takes about a fifth of a second, so the upload answer waits only briefly; AVIF takes a second or more. Every current browser and tool shows WebP.
- **The original is kept**, cleaned of metadata (UP-08), next to the version: in exports and backups, counted against the quota, deleted with the version, never served publicly. Images can be converted again later with better settings.
- **The file name follows the stored format.** A converted upload is `diagram.webp`; a new version in another format renames the asset (VER-02). Links with the old name keep working, because only the id counts ([ADR-0003](0003-Public-Link-Format.md)); version links of older versions keep their extension.
- **No negotiation by `Accept` any more:** every request for a link gets the same type. `?w=` resizes in the stored format. The AVIF background queue is removed.
- Existing assets stay as they are.

## Consequences

- The extension in a link and the delivered `Content-Type` match; answers no longer vary by `Accept`, which makes caches simpler.
- Browsers that cannot show WebP (none of the current ones) would need the owner to turn conversion off for the project.
- Storage per converted image grows by the original; the quota shows it.
- AVIF uploads are still served as AVIF; the server no longer creates AVIF itself. This supersedes the AVIF part of ADR-0011 and the format part of ADR-0015.
