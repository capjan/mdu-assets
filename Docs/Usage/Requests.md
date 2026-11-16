---
navigation.order: 50
description: How the server counts requests of an image, which pages refer to it, and what the numbers cannot show.
---

# Requests

The tiles, the list and the details show how often an image was requested and when last. **Requests** in the details also lists the pages that showed it.

## What is counted

- Successful public `GET` requests (`200` and `304`), with or without `?w=`.
- Not counted: `HEAD`, previews in the web interface, requests from signed-in sessions, and user agents that look like bots, crawlers or link previews.

Counts are collected in memory and written to the database every 10 seconds, so they can lag behind a little.

## Referring pages

From the `Referer` header the server keeps the origin and path of the page, without query and fragment, with a count and the last time. Each image keeps the 100 most recent pages.

Browsers usually send only the origin to other sites, so most entries name a site, such as `https://docs.example.com/`, rather than a page. MDU pages send the full page address for images, so their pages show up one by one ([ADR-0017](../Architecture/Architecture-Decisions/0017-Referrer-Policy-for-MDU-Pages.md)).

## What the numbers cannot show

Browser and CDN caches answer many requests without asking the server, so the count is a lower bound. It tells whether an image is still in use, not how many people saw it.

## Privacy

The server stores no addresses or user agents per request. Public links set no cookies.

## Images nobody uses

The filter **Never requested** or **Not in 90 days** in [Finding and tagging](Finding-and-Tagging.md#search-and-filters) shows images that pages no longer show, candidates for the trash.
