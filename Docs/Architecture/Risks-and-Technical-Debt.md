---
navigation.order: 11
description: Known risks, open questions and technical debt of the MDU Asset Server.
---

# Risks and Technical Debt

## Risks

| Risk | Impact | Handling |
| --- | --- | --- |
| NetVips under Native AOT | Image processing unusable in AOT builds | Mitigated: the [spike](NetVips-AOT-Spike.md) shows 0 AOT warnings and all checks green on every target. CI keeps running it. |
| Native library next to the program | No single-file binary; macOS signing must include libvips | Accepted. Archives and Docker images; sign and notarize program and library. |
| Builds under emulation | The .NET SDK crashes under x64 emulation | Build per architecture on native runners or cross-compile. |
| Public links are capability URLs | A shared link cannot be taken back except by deleting the image | Explained in the UI and documentation; deletion answers 410 immediately. |
| SVG as script carrier | XSS against users of the UI host | Sanitizing, sandboxing CSP, separate public host. |
| Request counts too low | Browser and CDN caches hide requests | Short `max-age`; the UI explains the limits. |
| No password prompt in some browsers | Basic Auth depends on the browser's own prompt; some embedded browsers show none and cannot open the web interface | Documented in the [deployment guide](../Deployment.md#sign-in); session sign-in (AUTH-09) removes it. Consider moving it before 1.0 |
| Referrers mostly origin only | Browsers send only the origin for cross-site image requests | See open question Q-03. |
| AVIF encoding cost | About one second per image, high memory use | Background queue with bounded workers. |
| SQLite write contention | Slow statistics or uploads under load | Batched statistics writes, WAL mode; sufficient for the planned team size. |

## Open questions

Tracked as issues with the label [`decision`](https://github.com/capjan/mdu-assets/issues?q=label%3Adecision) and answered by ADRs:

| ID | Question | Needed by |
| --- | --- | --- |
| Q-01 | May regular users create projects? Answered by [ADR-0014](Architecture-Decisions/0014-Project-Creation-by-Admin-Setting.md): only with an admin setting, off by default | M3 |
| Q-02 | Is a CDN purge hook needed when an image is replaced? | M6 |
| Q-03 | Should MDU set a referrer policy for asset images? | M7 |
| Q-04 | How many versions are kept by default? | M6 |
| Q-05 | How does an import handle ID collisions? | M9 |

## Technical debt

| Debt | Reason | Payback |
| --- | --- | --- |
| Basic Auth in the browser | No clean sign-out; after a password change the browser asks for the new password | Session login after 1.0 |
| Own SVG sanitizer next to MDU's icon reader ([ADR-0012](Architecture-Decisions/0012-Own-SVG-Sanitizer.md)) | Icons and complete files need different cleaning | Apply fixes to the allowlist in both places; share a package if the rules start to drift |
| Originals as previews | No thumbnails before image processing | Thumbnails in M5; the grid and list switch to them |
| Data protection keys stored unencrypted | Nothing protects data with them yet | Encrypt them before session sign-in (AUTH-09) uses cookies |
| Use cases in the endpoints | Too few for a separate core project; role checks are already in one place (`ProjectAccess`) | `MduAssets.Core` when versions arrive (M6) |
| Only empty projects can be deleted | Deleting images needs the trash, so they stay recoverable (PRJ-05) | Delete with confirmation once the trash exists (M6, [#36](https://github.com/capjan/mdu-assets/issues/36)) |
