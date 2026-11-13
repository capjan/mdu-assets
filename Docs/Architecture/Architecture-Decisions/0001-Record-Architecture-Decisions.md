---
description: Architecture decisions are recorded as one MADR file each.
---

# 0001 Record Architecture Decisions

- Status: Accepted
- Date: 2026-11-13

## Context

The asset server is built by a small team over several milestones. Reasons for decisions get lost quickly, and some decisions (link format, storage layout) can never be changed once links are public.

## Options

- No records, reasons only in pull requests
- A table in arc42 section 9
- One file per decision in the MADR style

## Decision

One file per decision in `Docs/Architecture/Architecture-Decisions`, numbered, in a short MADR form. The section README lists all records.

## Consequences

- Each decision can be linked from issues, pull requests and other sections.
- Records are not edited after acceptance except for status changes; a new record supersedes an old one.
