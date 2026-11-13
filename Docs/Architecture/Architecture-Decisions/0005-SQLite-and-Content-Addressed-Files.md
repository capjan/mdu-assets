---
description: Metadata lives in SQLite, file content in a content-addressed blob store on disk.
---

# 0005 SQLite and Content-Addressed Files

- Status: Accepted
- Date: 2026-11-13

## Context

The server runs as a single instance for a small team. Operation should need one container and one volume, and backups should be simple.

## Options

- File system and SQLite
- File system or S3, metadata in SQLite
- PostgreSQL and S3

## Decision

SQLite in WAL mode for all metadata, accessed with `Microsoft.Data.Sqlite` and hand-written SQL with numbered migrations. File content is stored once per SHA-256 under `blobs/ab/cd/<hash>`, written atomically. Derived variants are a deletable cache.

## Consequences

- No external services; backup is one consistent copy of the data directory.
- No horizontal scaling; a single instance is enough for the planned size.
- Identical content is stored once.
- The storage is behind an interface, so S3 can be added later if needed.
