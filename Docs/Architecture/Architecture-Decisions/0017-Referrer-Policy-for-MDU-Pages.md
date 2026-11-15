---
description: MDU keeps the browser's default referrer policy for asset images, so the asset server sees referring sites but not single pages.
---

# 0017 Referrer Policy for MDU Pages

- Status: Accepted (chosen by default while working through the milestones; revisit with the first users)
- Date: 2026-11-15

## Context

Open question Q-03 asked whether MDU should render asset images with `referrerpolicy="no-referrer-when-downgrade"`. Browsers send only the origin of a page with cross-site image requests by default (`strict-origin-when-cross-origin`), so the asset server learns which site shows an image, but not which page (STA-04). Sending the full page address would make the statistics more useful, but tells the asset server which page every reader opens. Documentation sites are often public, readers do not expect to be followed across pages, and NF-09 asks for no personal request data.

## Options

- MDU sets `referrerpolicy="no-referrer-when-downgrade"` on asset images
- MDU leaves the browser default
- MDU offers a setting in `mdu.yml`

## Decision

MDU keeps the browser default and sets no `referrerpolicy` on asset images. The asset server records the referring site and, where a browser sends it, the path; the interface explains that browsers usually report only the site. A setting in MDU can be added later if teams need page-level statistics for internal documentation.

## Consequences

- Readers' privacy stays as the browser defines it; nothing in MDU changes for M10.
- Referring pages are mostly sites (`https://docs.example.com/`), which still answers "where is this image used" at the level of documentation sites.
- Same-origin documentation, served from the asset server's host, would report full paths, but MDU sites are normally on another host.
