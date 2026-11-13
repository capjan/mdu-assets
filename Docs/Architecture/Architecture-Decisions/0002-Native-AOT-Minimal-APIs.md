---
description: The server is built with .NET 10 Native AOT and ASP.NET Core minimal APIs.
---

# 0002 Native AOT with Minimal APIs

- Status: Accepted
- Date: 2026-11-13

## Context

The server should start fast, use little memory, ship as a small container and as native binaries, and use the same platform as MDU. Contributors should only need .NET.

## Options

- ASP.NET Core with JIT, Razor Pages or MVC
- ASP.NET Core minimal APIs with Native AOT
- Another language or runtime (Go, Rust)

## Decision

.NET 10 with Native AOT and ASP.NET Core minimal APIs, using the request delegate generator, System.Text.Json source generation and the configuration binding source generator. Publishing treats trim and AOT warnings as errors.

## Consequences

- Small, self-contained binaries with fast start and low memory.
- No Razor, MVC, EF Core runtime model building or reflection-based serialization; libraries must be AOT-compatible.
- Native AOT does not cross-compile between operating systems, so every target is built on its own runner.
