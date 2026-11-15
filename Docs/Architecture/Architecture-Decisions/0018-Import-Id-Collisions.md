---
description: An import keeps asset ids where they are free and gives the others new ids, listed in the answer, instead of failing.
---

# 0018 Import Id Collisions

- Status: Accepted (chosen by default while working through the milestones; revisit with the first users)
- Date: 2026-11-15

## Context

Open question Q-05 asked what happens when a project export is imported into an instance that already used one of its asset ids (OPS-06). Ids are random, so between two independent instances a collision is very unlikely; it happens mainly when an export is imported into the instance it came from, for example to copy a project. Public links contain the id, so a changed id breaks links in documentation; failing the import would block copying projects and moving to a new instance after a partial migration.

## Options

- Fail the import when any id is taken
- Give colliding assets new ids and report a mapping
- Overwrite the existing asset

## Decision

The import keeps every id that this instance never used, including ids of deleted assets and of deleted projects (`retired_asset_ids`). Assets whose id is taken get a new random id. The answer of `POST /api/v1/projects/import` lists every changed id as old and new, and the web interface shows the list after the import, so authors can update their links. Existing assets are never overwritten.

## Consequences

- Moving a project to a new instance keeps all links once the host names point there.
- Copying a project within one instance works; the copy's links are new, as they must be, because the original keeps its links.
- Links in documentation that point at a changed id have to be updated by hand; `mdu lint` (M10) reports links that are unknown on the server.
