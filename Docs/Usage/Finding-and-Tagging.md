---
navigation.order: 30
description: Tag images, search while typing, filter, switch between tiles and list, and work with the keyboard.
---

# Finding and tagging

Images are organized with tags, not folders: one image can carry several tags, such as `setup`, `macos` and `v2` ([ADR-0020](../Architecture/Architecture-Decisions/0020-Tags-Instead-of-Folders.md)).

## Tags

The sidebar lists the tags of the project with their counts.

- **Filter:** click a tag to show only its images. Clicking more tags narrows the filter; an image must carry all of them. **Untagged** shows the images without any tag.
- **Tag one image:** type in the tag field of the details. Suggestions come from the tags of the project; ++enter++ or a comma adds a tag, ++backspace++ in the empty field removes the last one.
- **Tag many images:** select them (click the check box of a tile, or press ++space++ on a focused tile), then choose **Tags…**, or drag the selection onto a tag in the sidebar.
- **Rename, merge, delete:** the **⋯** button next to a tag in the sidebar renames it or removes it from all images. Renaming a tag to the name of another one merges both.

Uploads get the tags you are filtering by.

## Search and filters

The search field searches file name, alt text, description and tags while you type. ++down++ moves from the search field to the first image.

**Filters** narrows the list further:

| Filter | Choices |
| --- | --- |
| Type | Any type, or one format such as WebP or SVG |
| Uploaded | A period from and to a date |
| Uploader | Anyone, or one member |
| Requests | Never requested, or not requested in 30 days, 90 days or a year |

**Clear filters** resets search, tags and filters. Pasting an image link into the search field opens that image ([Links and snippets](Links-and-Snippets.md#from-a-link-back-to-the-image)).

## Tiles and list

The switch above the images shows them as tiles (preview, name, size, requests) or as a list. Click a column header in the list to sort by name, size, dimensions, upload date, last request or requests; click again to reverse the order.

## Details

A click on an image opens its details:

- A large preview with a light, dark or checkerboard background. Clicking it opens MDU's image viewer, where the mouse wheel, a trackpad pinch, ++plus++ and ++minus++ zoom, ++0++ fits the image, ++1++ shows it at its real size, and the arrow keys move it.
- The [snippets](Links-and-Snippets.md), file name, alt text, description and tags.
- Type, dimensions, size, original upload, hash, uploader and date.
- [Versions](Versions-and-Trash.md), [requests](Requests.md) and the light and dark partner.

## Keyboard

The arrow keys move between images, in the tiles and in the list, also before you have focused one.

| Key | In the images |
| --- | --- |
| Arrow keys, ++home++, ++end++ | Move to another image |
| ++enter++ | Open the details |
| ++space++ | Select or deselect |
| ++c++ | Copy the Markdown snippet |
| ++delete++ or ++backspace++ | Move the image, or the selection it belongs to, to the trash |
| ++cmd+v++ / ++ctrl+v++ | Upload the image in the clipboard |
