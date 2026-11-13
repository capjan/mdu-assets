---
navigation.order: 12
description: Terms used in the architecture documentation of the MDU Asset Server.
---

# Glossary

| Term | Definition |
| --- | --- |
| ADR | Architecture decision record: one file per significant decision with context, options, decision and consequences. |
| Asset | An uploaded image with all its versions, metadata and statistics; the unit behind one public link. |
| Asset ID | Random base62 ID of at least 10 characters that identifies an asset for good. |
| Blob | Stored file content, named after its SHA-256 hash. |
| Capability URL | A URL that grants access by being known, without authentication. |
| Light/dark pair | Two assets linked as the light and dark variant of the same picture; their snippet uses `#only-light` and `#only-dark`. |
| MDU | Markdown Documentation Utility, the `mdu` .NET tool that renders Markdown to static sites. |
| Native AOT | Ahead-of-time compilation of .NET code to a native program without a JIT. |
| Project | Container for assets and folders with its own members and roles. |
| Public host | Host name that serves only public links `/i/…`. |
| Snippet | Ready-to-paste text for an asset, such as a Markdown image with alt text. |
| UI host | Host name that serves the web interface and the API. |
| Variant | A derived file of a version, for example 800 px wide or AVIF; cached and re-creatable. |
| Version | One immutable content of an asset. |
