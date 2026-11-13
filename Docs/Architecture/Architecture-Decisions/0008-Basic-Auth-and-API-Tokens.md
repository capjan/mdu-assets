---
description: Users sign in with Basic Auth for now; tools use personal API tokens.
---

# 0008 Basic Auth and API Tokens

- Status: Accepted
- Date: 2026-11-13

## Context

A simple sign-in is enough for the start. External tools must not store user passwords.

## Options

- Basic Auth for UI and API
- Basic Auth plus personal API tokens
- Session login, OIDC or passkeys from the start

## Decision

Basic Auth over HTTPS for the UI and the API, with PBKDF2-SHA256 password hashes. Tools use personal API tokens: named, expiring, optionally limited to one project and to read or write, shown once and stored as a hash. Session login, passkeys and OIDC follow after 1.0.

## Consequences

- Fast to build; works with curl and every HTTP client.
- No clean sign-out in browsers until session login exists.
- Passwords never end up in tool configurations.
