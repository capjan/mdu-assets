---
navigation.order: 20
description: Public links that never change, the snippets for Markdown and HTML, light and dark pairs, sizes and MDU switches.
---

# Links and snippets

Every image has a public link on the public host:

```text
https://img.example.com/i/k3Fq9xZ2aB/app-settings.webp
```

Only the id `k3Fq9xZ2aB` counts. `/i/k3Fq9xZ2aB` and `/i/k3Fq9xZ2aB/any-name` return the same image, so old links keep working after a rename. Tags, renaming the project and new versions never change the link. Ids are random, at least 10 characters long, and cannot be guessed from other ids.

The link always returns the current version. Browsers and caches may reuse it for 5 minutes and then ask again, so a new version shows up within minutes. Each version also has a link of its own, `/i/k3Fq9xZ2aB/v2/app-settings.webp`, that never changes.

## Snippets

The details show the snippets with a copy button each:

| Snippet | Example |
| --- | --- |
| Markdown | `![App settings](https://img.example.com/i/k3Fq9xZ2aB/app-settings.webp)` |
| URL | `https://img.example.com/i/k3Fq9xZ2aB/app-settings.webp` |
| HTML | `<img src="…" alt="App settings" width="1280" height="800">` |

## Snippet options

The switches under the snippets add MDU attributes to the Markdown. The interface remembers your last choice.

| Option | Adds |
| --- | --- |
| Frame | `{.image-frame}` |
| Zoom on / off | `{.image-zoom-on}` or `{.image-zoom-off}` for MDU's image viewer |
| Width | `?w=800` to the link |
| Caption | An MDU figure: the image between `^^^` lines with the caption after the closing one; in HTML a `<figure>` with `<figcaption>` |

`?w=` returns a smaller copy in the same format, never a larger one. Only the configured widths exist (by default 320, 640, 800, 1024, 1280, 1600 and 2048); other values snap to the next larger one.

## Light and dark pairs

Two images can form a pair, one for the light and one for the dark mode of MDU pages. Link them in **Light and dark** in the details of one of them. When you upload two files named like `settings-light.png` and `settings-dark.png`, the interface suggests the pair. Then the Markdown snippet holds both lines with the same alt text:

```markdown
![Settings](https://img.example.com/i/k3Fq9xZ2aB/settings-light.webp#only-light)
![Settings](https://img.example.com/i/Pq81LmZ0cD/settings-dark.webp#only-dark)
```

## From a link back to the image

Paste an image link into the search field to open that image, also with a `?w=` size or a `#only-dark` fragment. This works for every image in projects you can see.

## What visitors get

Public links need no sign-in and set no cookies. They answer with `ETag` and `Last-Modified`, support `HEAD` and range requests, and serve SVG with a content security policy that blocks scripts. An image in the trash answers `410 Gone`, an unknown id `404`.
