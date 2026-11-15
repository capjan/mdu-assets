---
description: The server picks lossy or lossless WebP by content right after upload, keeps only that file, and no longer switches formats per browser.
---

# 0019 Format Chosen on Upload

- Status: Accepted
- Date: 2026-11-15, revised the same day after the first screenshots: choice by content, upload not kept, lossless option

## Context

Until now the server stored the upload and, per request, served a WebP or AVIF copy to browsers that accepted it ([ADR-0011](0011-AVIF-in-the-Background.md), [ADR-0015](0015-Image-Variant-Rules.md)). The link kept the extension of the upload, so `screenshot.png` often delivered WebP, and every answer carried `Vary: Accept`. Replacing an image with another format left the old extension in the link, too. Authors want one file behind a link, in the format that is the best trade-off between size and quality, with the matching extension, decided once.

## Options

- Keep negotiating by `Accept` per request, and only fix the extension on new versions
- Choose the format right after upload; keep the upload or discard it
- Choose lossy or lossless by file type, or by content
- Convert to WebP only, or also to AVIF when that is smaller

## Decision

- **On upload and new versions** (UP-11), when the project allows conversion (on by default), the server encodes the cleaned image as WebP and keeps that only if it is smaller:
  - JPEG (photos): lossy WebP, quality 82, with smart chroma subsampling.
  - PNG, BMP and still GIF: lossless WebP, unless that is more than twice the size of the lossy WebP. Screenshots of interfaces, text and diagrams stay lossless and sharp; photographic content (also a screenshot of a photo) becomes lossy, where lossless is five to ten times larger. A 2728 × 1280 screenshot of a landscape was 3.7 MB lossless and 0.44 MB lossy.
  - With **lossless** chosen for an upload (`?lossless=true`, a toggle in the web interface), nothing loses quality: PNG, BMP and GIF only become lossless WebP, JPEG stays as uploaded.
  - SVG, AVIF, WebP, animations, images with more than 8 bits per channel and images larger than 16383 pixels keep their format.
- **WebP, not AVIF:** WebP takes about a fifth of a second, so the upload answer waits only briefly; AVIF takes a second or more. Every current browser and tool shows WebP.
- **Only the converted file is stored.** The type and size of the upload are recorded for the interface ("Uploaded as PNG, 25 MB"). The first version of this decision kept the upload too; versions from that time keep it until they are deleted. Because only the result counts against the quota, the default upload limit rises to 50 MB, so a screenshot of a whole 5K monitor fits.
- **The file name follows the stored format.** A converted upload is `diagram.webp`; a new version in another format renames the asset (VER-02). Links with the old name keep working, because only the id counts ([ADR-0003](0003-Public-Link-Format.md)); version links of older versions keep their extension.
- **No negotiation by `Accept` any more:** every request for a link gets the same type. `?w=` resizes in the stored format. The AVIF background queue is removed.
- Existing assets stay as they are.

## Consequences

- The extension in a link and the delivered `Content-Type` match; answers no longer vary by `Accept`, which makes caches simpler.
- Browsers that cannot show WebP (none of the current ones) would need the owner to turn conversion off for the project.
- A lossy conversion cannot be undone on the server; authors who need every pixel choose lossless for that upload.
- Choosing by content encodes graphics twice, about half a second more for a large screenshot.
- AVIF uploads are still served as AVIF; the server no longer creates AVIF itself. This supersedes the AVIF part of ADR-0011 and the format part of ADR-0015.
