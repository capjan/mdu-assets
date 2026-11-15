---
description: Tags replace nested folders as the way to organize images; existing folders became tags.
---

# 0020 Tags Instead of Folders

- Status: Accepted
- Date: 2026-11-15

## Context

Until now a project had nested folders and free tags (ORG-01 – ORG-03). An image could be in one folder only, while documentation images often belong to several topics at once: a screenshot of the macOS setup in dark mode is "Setup", "macOS" and "Dark Mode". Folders forced one place, so authors had to choose, and the sidebar, uploads, drag and drop and exports all depended on them. Tags already existed with several per image, suggestions and comparison without case.

## Options

- Keep folders and add multi-tag filters next to them
- Replace folders with tags and convert existing folders
- Hierarchical tags such as `app/settings`

## Decision

- **Tags are the only way to organize a project.** The sidebar lists them with their use; a filter with several tags shows images that have all of them (AND), so each tag narrows the list; "Untagged" shows the rest.
- **Tagging many images:** dragging images onto a tag, or "Tags…" on a selection, adds and removes tags at once. A tag can be renamed, merged into another one by renaming it to that name, or deleted in the whole project. Uploads get the tags the list is filtered by, as they went into the open folder before.
- **Flat tags,** no hierarchy: AND filters with flat tags narrow down just as well and stay simple.
- **Conversion:** migration 0011 gives every image its folder and all folders above it as tags (`Guides/Setup` becomes `Guides` and `Setup`); a tag the project already uses keeps its spelling. Imports of format 1 exports do the same; exports are format 2 without folders.
- The API loses the folder endpoints, `?folder=`, `?subfolders=`, `assets/move` and `folderId`. This is a breaking change before 1.0, listed in the release notes.
- SQLite cannot drop `assets.folder_id`, which has a foreign key, without rebuilding the assets table. The column and the `folders` table stay, always empty, until a later table rebuild removes them.

## Consequences

- An image can belong to several topics, and filters combine them.
- Links never depended on folders, so nothing changes for readers (ADR-0003).
- There is no tree any more; authors who liked a path can use tags such as `Guides` and `Setup` together.
- Two unused schema objects stay in the database for now.
