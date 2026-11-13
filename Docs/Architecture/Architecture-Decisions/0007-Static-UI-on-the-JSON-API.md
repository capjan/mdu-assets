---
description: The web interface consists of embedded static files that use only the JSON API.
---

# 0007 Static UI on the JSON API

- Status: Accepted
- Date: 2026-11-13

## Context

Razor Pages and MVC are not available under Native AOT. Contributors should not need a Node.js toolchain. The interface must look like MDU.

## Options

- Embedded static HTML, CSS and plain JavaScript on the JSON API
- Server-side templates with a little JavaScript
- Blazor WebAssembly

## Decision

Embedded static files with plain JavaScript modules and no build step. The interface uses only the public JSON API.

## Consequences

- Every UI feature is available to tools through the API as well.
- No framework; components are written by hand in the MDU style.
- Tests of the API cover most behavior; UI checks focus on interaction and accessibility.
