---
navigation.order: 10
description: Upload images by drag and drop, paste or file picker, and how the server picks the format of each image.
---

# Uploading

Editors and owners of a project upload in three ways:

- **Drop** one or more files anywhere on the project window. A drop zone names the target project.
- **Paste** an image from the clipboard with ++cmd+v++ or ++ctrl+v++. A pasted image without a name gets one like `screenshot-2026-11-13-1742.png`.
- **Choose files** with the upload button, also on phones.

Several files upload at once, each with its own progress. Uploads get the tags you are currently filtering by, so uploading while the tag `setup` is selected tags the new images with `setup`.

After the upload the details open with the snippet. Set the file name and the alt text there; the alt text is suggested from the file name (`app-settings.png` becomes "App settings"). For several files, **Copy all** copies every snippet at once.

## Allowed files

PNG, JPEG, GIF, WebP, AVIF, BMP and SVG, recognized by their content, not only by the extension. The largest upload is 50 MB unless the administrator or the project sets a lower limit, and images may have at most 50 million pixels.

- **Raster images** lose their EXIF, GPS and other metadata. The color profile stays, and the orientation is applied first.
- **SVG** is cleaned: scripts, event handlers, external references and `foreignObject` are removed ([ADR-0012](../Architecture/Architecture-Decisions/0012-Own-SVG-Sanitizer.md)).

## The format is chosen on upload

Right after the upload, the server picks the format with the best balance of size and quality, and stores only that file ([ADR-0019](../Architecture/Architecture-Decisions/0019-Format-Chosen-on-Upload.md)):

| Upload | Stored as |
| --- | --- |
| JPEG photo | Lossy WebP at high quality |
| PNG, BMP, still GIF (screenshots, graphics) | Lossless WebP; lossy WebP when the content is photographic (lossless would be more than twice the size) |
| SVG, AVIF, WebP, animated GIF | Unchanged |

A conversion happens only when the file gets smaller. The file name and its extension follow the stored format, so `diagram.png` becomes `diagram.webp`, and the details show the type and size of the original upload.

Turn on **Lossless** before uploading when every pixel matters, for example for a detailed diagram: nothing loses quality, and JPEG stays as it is. Owners can turn conversion off for a whole project in **Project settings**; images already stored stay as they are.

## Duplicates

When a file with the same content already exists in the project, the server says so and offers the existing image and its link instead of storing a second copy.

## Limits

An upload that is larger than the limit or the remaining quota is refused before it is transferred completely, with a message that names the limit. The project's usage is shown in **Project settings**; from 90 % of the quota the interface warns.
