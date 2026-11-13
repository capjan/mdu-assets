---
navigation.order: 10
navigation.placement: top
description: arc42 architecture documentation of the MDU Asset Server.
---

# Architecture

This is the architecture documentation of the MDU Asset Server, structured after the [arc42](https://arc42.org) template. It describes the planned system and is maintained together with the code: a pull request that changes the architecture updates the matching section or adds a decision.

| # | Section | Contents |
| --- | --- | --- |
| 1 | [Introduction and Goals](Introduction-and-Goals.md) | Purpose, top quality goals, stakeholders |
| 2 | [Constraints](Constraints.md) | Technical, organizational and convention constraints |
| 3 | [Context and Scope](Context-and-Scope.md) | Users, neighboring systems and interfaces |
| 4 | [Solution Strategy](Solution-Strategy.md) | Key approaches for the quality goals |
| 5 | [Building Block View](Building-Block-View.md) | Static structure of the server |
| 6 | [Runtime View](Runtime-View.md) | Upload, delivery, replacement and background processing |
| 7 | [Deployment View](Deployment-View.md) | Docker on a VPS behind a reverse proxy |
| 8 | [Crosscutting Concepts](Crosscutting-Concepts.md) | Domain model, security, persistence, HTTP, UI, testing |
| 9 | [Architecture Decisions](Architecture-Decisions/README.md) | Architecture decision records (ADRs) |
| 10 | [Quality Requirements](Quality-Requirements.md) | Quality tree and scenarios |
| 11 | [Risks and Technical Debt](Risks-and-Technical-Debt.md) | Known risks and open questions |
| 12 | [Glossary](Glossary.md) | Terms used in this documentation |

The detailed functional requirements with their IDs (such as `LNK-01`) are in the [requirements document](../Requirements.md), which is written in German.

## Keeping it current

- A change to the structure, an interface, the deployment or a crosscutting concept updates the matching section in the same pull request.
- A decision that is hard to reverse gets an [ADR](Architecture-Decisions/README.md). A decision that replaces an earlier one marks the old ADR as superseded instead of editing it.
- Planned parts are marked as planned. When the code lands, the section describes the code as it is.
