---
description: Admins create projects; other users only when an admin turns on a setting, which is off by default.
---

# 0014 Project Creation by Admin Setting

- Status: Accepted
- Date: 2026-11-14

## Context

Open question Q-01 asked whether regular users may create projects or only admins. PRJ-01 already mentions a setting for it. Every project uses storage and gets its own owners, so an instance for a small team wants to keep the list of projects tidy, while a larger team may not want an admin in the loop for every new documentation project.

## Options

- Only admins create projects
- Every signed-in user creates projects
- Admins create projects; an admin setting lets all users do it as well

## Decision

Admins always create projects. Other users create projects only when the instance setting "All users may create projects" is on; it is off by default. Whoever creates a project becomes its owner. The setting is stored in the `settings` table and changed in the admin area or with `PATCH /api/v1/settings`.

## Consequences

- New instances behave like "only admins"; nothing to configure for small teams.
- Larger teams turn the setting on once and need no admin for new projects.
- `GET /api/v1/me` returns `canCreateProjects`, so the interface shows "New project" only to those who may use it.
- Quotas per project (OPS) will limit storage regardless of who created the project.
