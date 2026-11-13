---
description: Image processing uses NetVips and the native libvips library.
---

# 0006 NetVips for Image Processing

- Status: Accepted
- Date: 2026-11-13

## Context

The server strips metadata, creates thumbnails and resized variants and converts to WebP and AVIF. It must work under Native AOT.

## Options

- NetVips/libvips: fast, WebP and AVIF, LGPL, native library
- SkiaSharp: MIT, native library, no AVIF encoding
- ImageSharp: pure .NET, Six Labors Split License, no AVIF

## Decision

NetVips with NetVips.Native. The [NetVips AOT spike](../NetVips-AOT-Spike.md) built without AOT warnings and passed every check on linux-x64, linux-arm64 and osx-arm64, including AVIF.

## Consequences

- libvips (17–18 MB) ships next to the program; no single-file binary.
- On macOS, the program and libvips are signed and notarized.
- LGPL is met because libvips is loaded dynamically and replaceable; license texts ship with releases.
- CI keeps running the spike so updates that break AOT show up early.
