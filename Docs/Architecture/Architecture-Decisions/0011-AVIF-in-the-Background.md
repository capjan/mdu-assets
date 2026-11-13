---
description: AVIF variants are encoded by background workers, never while a request waits.
---

# 0011 AVIF in the Background

- Status: Accepted
- Date: 2026-11-13

## Context

The spike measured about one second and high memory use to encode a 1600 × 900 image as AVIF, against about 170 ms for WebP and 20 ms for a thumbnail.

## Options

- Encode on the first request
- Encode in the background and serve another format until ready
- No AVIF

## Decision

A bounded in-process queue encodes AVIF variants after upload and for newly requested widths. Until a variant is ready, the server delivers WebP or the original. Thumbnails and WebP may be created synchronously.

## Consequences

- No slow first requests; memory stays bounded by the number of workers.
- The first readers may get WebP instead of AVIF.
- Lost queue entries after a restart are recreated on demand.
