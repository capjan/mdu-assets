---
navigation.order: 40
description: Replace an image without changing its link, return to an older version, and delete and restore images.
---

# Versions and trash

## Replace an image

Drop a new file onto the details, paste one there, or choose **Replace image…**. The link stays, and the version number goes up. Every page that shows the image gets the new version within minutes; version links of older versions keep showing the old one.

The replacement may have another format, for example a PNG for a WebP, or a JPEG that is converted. The file name then gets the new extension. Links with the old name keep working, because only the id counts.

**Lossless** next to **Replace image…** keeps every pixel, as on [upload](Uploading.md#the-format-is-chosen-on-upload).

## Versions

**Versions** in the details lists every version with date, user, size and dimensions. **Show older** loads more.

- Open an old version through its version link.
- **Restore** makes an old version current again, as a new version.
- Owners delete single old versions for good.

Old versions count toward the project's quota. Owners can limit how many versions are kept in **Project settings**; older ones are deleted when an image is replaced.

## Trash

**Move to trash** in the details, or ++delete++ on a selected image, moves images to the trash. If an image was requested in the last 30 days, the interface warns first with the number of requests and the pages that showed it.

Images in the trash answer `410 Gone` on their link. **Trash** in the sidebar lists them:

- **Restore** brings an image back with the same link.
- After 30 days (configurable) images are deleted for good automatically.
- Owners and administrators delete single images or **Empty trash** at once, after a confirmation. This cannot be undone.

Images in the trash count toward the quota until they are deleted for good.
