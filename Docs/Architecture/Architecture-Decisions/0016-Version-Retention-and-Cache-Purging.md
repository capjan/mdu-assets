---
description: All versions are kept unless an owner sets a limit per project, and the server offers no CDN purge hook because current links revalidate within minutes.
---

# 0016 Version Retention and Cache Purging

- Status: Accepted (chosen by default while working through the milestones; revisit with the first users)
- Date: 2026-11-15

## Context

Open questions Q-02 and Q-04 asked how many versions an asset keeps by default and whether the server should purge a CDN when an image is replaced. Versions are cheap where screenshots change a little, and expensive where large photos are replaced often. Current links are served with `max-age=300` and `stale-while-revalidate=86400` (DLV-02); version links are `immutable`. Variants are stored per blob, so a new version gets new variants and the old ones stay for the version links.

## Options

- Keep all versions, keep a fixed number, or keep all with an optional limit per project
- Offer a purge hook for CDNs, or rely on the short `max-age` of current links

## Decision

- **Q-04:** every version is kept by default. Owners can set "Versions to keep" per project; when an image is replaced, older versions beyond that number are deleted for good. Owners can also delete single old versions. Old versions count toward the quota (M9).
- **Q-02:** variants of an old version are not discarded when the image is replaced, because the old version link still serves them; they go when their blob is no longer used by any version. The server offers no CDN purge hook: current links revalidate after five minutes, and version links never change. A CDN in front of the public host should respect `Cache-Control` and `Vary: Accept`.

## Consequences

- Nothing is lost by replacing an image, and documentation that pins a version keeps showing it.
- Projects that replace large images often should set a version limit; the quota (M9) makes the cost visible.
- A replaced image reaches readers behind a CDN within about five minutes, as without a CDN. A purge hook can be added later without changing links.
