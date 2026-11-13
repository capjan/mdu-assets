---
description: Uploaded SVG files are cleaned by a sanitizer of the asset server that follows the allowlist of MDU's icon reader.
---

# 0012 Own SVG Sanitizer

- Status: Accepted
- Date: 2026-11-13

## Context

Uploaded SVG files must not run script or load anything from elsewhere (UP-07). MDU already cleans SVG files for custom icons. The requirements suggested sharing that code, either as a copy with a drift test or as a shared package.

MDU's reader turns an SVG file into the body of an inline icon: it prefixes all ids and classes, scopes style sheets, replaces `use` with copies and removes the root's size attributes. The asset server serves complete SVG files that browsers open as images or as documents, so most of that work does not apply and would change the files.

## Options

- Copy MDU's icon reader and check for drift
- Move the reader into a package shared by MDU and the asset server
- Write a sanitizer for complete files with the same allowlist and the same CSS rules

## Decision

The asset server has its own `SvgSanitizer` in `MduAssets.Imaging`. It keeps MDU's element allowlist (plus `title` and `desc` for accessibility), removes event handlers and unsafe animations, allows references only into the file or to embedded raster images, and removes imports, external URLs and image functions from CSS, including escaped forms. It never resolves DTDs or entities. Public links serve SVG with a sandboxing content security policy as a second line of defense.

## Consequences

- Sanitized files keep their structure, ids and styles, so they look as uploaded.
- Fixes to MDU's allowlist must be applied here as well; the class comment points to MDU's reader.
- Tests cover the known payloads (script, `foreignObject`, `javascript:` links, `set` on `href`, external `use`, `@import`, CSS escapes, XXE).
