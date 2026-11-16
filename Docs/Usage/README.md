---
navigation.order: 30
description: Sign in, find your way around the web interface, and get from a screenshot to a Markdown link.
---

# Usage

The web interface runs on the UI host, for example `https://assets.example.com/`. Every function in it is also available through the [API](../API.md).

## Sign in

The sign-in page asks for user name and password. **Stay signed in** keeps the session for 14 days without use; otherwise it ends when the browser closes. An administrator hands you your first password; the server asks for a new one at the first sign-in.

The user name in the header opens a menu with **Account**, **Admin** (for administrators) and **Sign out**. Locking or deleting a user, and changing a password, end that user's sessions at once.

## Find your way around

| Part | Contents |
| --- | --- |
| Sidebar | Your projects, the tags of the current project with their counts, **Trash** and **Project settings** |
| Header | Search, the switch for light, dark or automatic mode, and the user menu |
| Images | All images of the project as tiles or as a list, filtered by the search and the chosen tags |
| Details | A large preview, the snippets, the metadata, versions and requests of one image |

## From screenshot to page

1. Take a screenshot and press ++cmd+v++ or ++ctrl+v++ in the project, or drop files anywhere on the window.
2. The server converts the image, and the details open with the Markdown snippet. Where the browser allows it, the snippet is already in the clipboard.
3. Paste it into your page: `![App settings](https://img.example.com/i/k3Fq9xZ2aB/app-settings.webp)`.

The link stays the same when you rename the image, tag it or replace it with a new version.

| Page | Topic |
| --- | --- |
| [Uploading](Uploading.md) | Drop, paste, formats, conversion to WebP, duplicates |
| [Links and snippets](Links-and-Snippets.md) | Markdown, HTML, light and dark pairs, sizes and MDU switches |
| [Finding and tagging](Finding-and-Tagging.md) | Tags, search, filters, views and keyboard |
| [Versions and trash](Versions-and-Trash.md) | Replace an image, go back to an older version, delete and restore |
| [Requests](Requests.md) | How often an image is requested and from which pages |
| [Account](Account.md) | Password, API tokens and integrations |
