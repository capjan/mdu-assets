---
description: The MDU design tokens are copied into the server and checked for drift.
---

# 0010 MDU Design Tokens

- Status: Accepted
- Date: 2026-11-13

## Context

The interface should match MDU exactly, including light and dark mode, without coupling the server to MDU's full stylesheet.

## Options

- Copy `tokens.css` from the MDU design system and build own components on it
- Include MDU's complete `main.css`
- Publish the design resources as a shared package

## Decision

Copy `tokens.css` unchanged. A test compares it with a pinned version, and a .NET tool updates the copy. Components follow the design system documentation, including the MDU focus style.

## Consequences

- Small, independent stylesheet; drift is visible in CI.
- Updates to MDU's tokens need a deliberate update here.
