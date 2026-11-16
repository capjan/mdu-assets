---
navigation.order: 60
description: Change your password, create API tokens for tools, and download a ready ShareX configuration.
---

# Account

**Account** in the user menu holds your password, your API tokens and the integrations.

## Password

**Change password** asks for the current and the new password, twice. Changing it signs you out everywhere else.

## API tokens

Tools such as `mdu`, scripts and ShareX sign in with a personal API token instead of your password.

1. Enter a name, for example `laptop mdu`.
2. Choose the project (or all your projects), the access (**Read only** or **Upload and read**) and the expiry (30 days, 90 days, a year, or never).
3. Choose **Create token**. The token, starting with `mdua_`, is shown once; copy it now.

A token never has more rights than you. The list shows name, prefix, expiry and last use; delete a token there to revoke it at once. Locking your user stops all your tokens too.

## Integrations

**Integrations** appears after you create a token and prepares the tools for one project, with that token filled in:

- A **ShareX** configuration (`.sxcu`) for Windows: import it, and every screenshot is uploaded and its Markdown snippet lands in the clipboard.
- A **curl** command that uploads a file and prints its Markdown snippet.

For MDU sites, see [Integrations](../Integrations.md).
