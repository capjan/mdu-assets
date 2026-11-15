---
description: Browsers sign in on the server's own page and get a session cookie; Basic credentials remain only for tools.
---

# 0021 Sign-In Page and Session Cookie

- Status: Accepted
- Date: 2026-11-15

## Context

The web interface used HTTP Basic authentication ([ADR-0008](0008-Basic-Auth-and-API-Tokens.md)): the browser showed its own password prompt, could not sign out, asked again after every password change, and some embedded browsers showed no prompt at all. It looks unfinished next to the MDU design. AUTH-09 planned a sign-in form with a session for later.

## Options

- Keep Basic Auth and document its limits
- A sign-in page with a server-side session store
- A sign-in page with an encrypted session cookie (ASP.NET Core cookie authentication)

## Decision

- **Sign-in page** at `/login` in the MDU design; `/` without a session sends the browser there and comes back to the same address afterwards (only addresses on this server). `POST /api/v1/session` checks name and password with the same throttle and audit log as before; `DELETE /api/v1/session` signs out. The account menu behind the user name in the header has Account, Admin and Sign out.
- **Session cookie** `__Host-mdu-assets-session`: encrypted with ASP.NET Core data protection, `HttpOnly`, `Secure`, `SameSite=Strict`, path `/`, no domain. Without "Stay signed in" it ends with the browser; with it, after 14 days without use.
- **Checked on every request:** the cookie carries the user id and a fingerprint of the password hash. A deleted or locked user, or a changed password, ends every session of that user at once; a password change keeps the session it was made in. Admin rights and start passwords apply from the next request.
- **Cross-site requests:** `SameSite=Strict` and the existing check of `Sec-Fetch-Site` and `Origin` refuse changes, including sign-ins, that another site starts.
- **Tools keep their way in:** API tokens (`Authorization: Bearer`) from anywhere, Basic credentials only from tools: browsers always send `Sec-Fetch-Site`, so Basic credentials that a browser remembers from before are ignored, and signing out works. A 401 never carries `WWW-Authenticate`, so no browser shows its prompt.
- **Keys:** the data protection keys stay in `keys/` (only the server user may read it) and are no longer part of `mdu-assets backup`. A stolen backup therefore cannot make session cookies; a restored instance makes new keys and everybody signs in again. Encrypting the keys with a certificate would add a secret that has to live somewhere else and was not worth it.
- No server-side session store: SQLite stays the only state, and the per-request user check gives the same instant revocation.

## Consequences

- No browser prompt, a real sign-out, and password changes without a second prompt.
- Whoever can read `keys/` on the server can make session cookies; that person can already read the database and the images.
- Supersedes the browser part of ADR-0008; Basic credentials and tokens for tools stay as decided there.
