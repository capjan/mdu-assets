---
navigation.order: 40
description: Manage users, projects, members, limits and quotas, and read the audit log.
---

# Administration

## Roles

Administrators manage users and see every project. Everyone else sees only the projects they are a member of, with one of three roles:

| Action | Viewer | Editor | Owner |
| --- | :-: | :-: | :-: |
| View images, tags and requests, copy snippets | ✓ | ✓ | ✓ |
| Upload, replace, edit, tag, move to the trash and restore | | ✓ | ✓ |
| Rename, merge and delete tags | | ✓ | ✓ |
| Manage members, rename, export and delete the project | | | ✓ |
| Delete old versions and empty the trash for good | | | ✓ |

A project always keeps at least one owner. Administrators have owner rights in every project without being members.

## Users

**Admin** in the user menu lists the users.

- **Create user:** enter a name, optionally make the user an administrator, and hand over the start password that appears once. The user chooses an own password at the first sign-in; until then nothing else works.
- **Lock** stops access at once: sessions and tokens of the user are refused from the next request on. **Unlock** reverses it.
- **Reset password** issues a new start password and ends the user's sessions.
- The last unlocked administrator cannot lose the role or be locked.

Failed sign-ins are limited per user name and address: after 10 failures within 15 minutes, further tries are refused for a while.

## Projects

Administrators create projects with **New project** in the sidebar: a name, a short name for API paths, and a description. To let every user create projects, turn on **All users may create projects** in the admin settings ([ADR-0014](Architecture/Architecture-Decisions/0014-Project-Creation-by-Admin-Setting.md)). Whoever creates a project becomes its owner.

The short name appears in API paths and never changes. Renaming a project changes only its display name; image links stay the same.

**Admin** also shows every project with its number of images, storage, trash and last change, and the total storage of the server.

## Project settings

Owners open **Project settings** in the sidebar:

| Section | Contents |
| --- | --- |
| Name and description | Display name and description |
| Conversion | Whether uploads are converted to WebP ([Uploading](Usage/Uploading.md#the-format-is-chosen-on-upload)) |
| Versions to keep | How many versions an image keeps; 0 or empty keeps all. Older ones are deleted when an image is replaced |
| Largest upload | A lower limit than the server's for this project |
| Storage quota | Set by administrators only. Counts current images, old versions and the trash, not resized copies. From 90 % the interface warns; uploads beyond it are refused |
| Members | Add a member by user name with a role, change roles, remove members |
| Export | A zip with the current or all versions and the metadata, see [Operations](Operations.md#move-a-project-to-another-server) |
| Delete project | Only a project without images in use: move them to the trash first. Type the short name to confirm; the trash is emptied for good with the project |

## Audit log

The server records sign-ins (also failed ones), uploads, new versions, changes, deletions and restores, and changes to members, tokens and users. Administrators see the whole log under **Admin**, owners the log of their project in **Project settings**. Entries are kept for 365 days (configurable, see [Configuration](Configuration.md#statistics-limits-and-retention)).
