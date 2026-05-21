---
id: 0015
title: Responsibility-Driven Design — roles, responsibilities, and collaborators drive domain modeling
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0015 — Responsibility-Driven Design

## Context

We have a strong patterns catalog (`docs/architecture/patterns.md`) that covers *how* to write code: CQRS, pipeline middleware, repositories, adapters, money handling. We do not yet have a discipline for *what* the domain model should look like — which objects exist, what they're responsible for, what they collaborate with. Without that, the team will drift into ad-hoc decisions inside individual feature files, and the same concept will get implemented three slightly different ways across handlers.

Cleansia avoided this by accident: the developer (the user) had a strong intuition about responsibilities. We want to make the intuition explicit and reviewable, especially with multiple agents implementing concurrently.

## Decision

Adopt **Responsibility-Driven Design** (Wirfs-Brock) as the design discipline that sits *above* the patterns catalog. Every aggregate, value object, domain service, and adapter is described as a **role** with:

- **Responsibility:** one or two sentences naming what this role is accountable for. If you can't say it in two sentences, the role is doing too much; split.
- **Collaborators:** the other roles this role talks to. A short, named list. Collaborators are typed by their *role*, not their implementation class.
- **Knows:** what state this role owns or has authoritative access to.
- **Does NOT know:** explicit anti-responsibilities. These prevent scope creep at design time.

### Where roles live

```
docs/architecture/roles/
├── README.md                    # index of all roles
├── customer.md
├── maker.md
├── order.md
├── order-pricing.md
├── product.md
├── invoice.md
├── invoice-numbering.md
├── payout-batch.md
├── country-configuration.md
├── auth-service.md
├── payment-provider.md          # role; implementations are adapters
├── shipping-carrier.md
├── company-registry.md
├── email-provider.md
├── address-geocoder.md
├── blob-storage.md
└── ...
```

Each file is one page maximum. If a role outgrows one page, the role is too big.

### Role file template

```markdown
---
role: <RoleName>
kind: aggregate | value-object | domain-service | adapter | repository | application-service
status: draft | accepted
---

# <RoleName>

## Responsibility
One or two sentences. The responsibility statement must complete the sentence
"This role exists to ..."

## Collaborators
- **<OtherRole>** — what we ask of it
- **<OtherRole>** — what we ask of it

## Knows
- <state or authoritative reference>

## Does NOT know
- <explicit anti-responsibility>
- <another anti-responsibility>

## Lifecycle
How instances come into being and go away. For aggregates, typically:
- Created by: <factory or command>
- Modified by: <commands>
- Persisted by: <repository>
- Destroyed by: <command or never>

## Implementation pointer
Code path(s) once implemented: `backend/src/Makables.Core.Domain/<area>/<RoleName>.cs`
```

### How RDD slots into the existing pipeline

| Existing step | RDD contribution |
|---|---|
| **Batch 4–5 ADRs** (Integration, NFR) | When an ADR introduces a new role (e.g. `PaymentProvider`), the ADR creates the role file. ADR cites `docs/architecture/roles/<role>.md`. |
| **Batches 6–8 user stories** | Each story lists the roles it uses or extends. The roles block sits between the narrative and the AC. New roles get created in `docs/architecture/roles/` as part of writing the story. |
| **Batch 9 tickets** | A ticket may modify a role's responsibilities. If so, the ticket updates the role file. Reviewer checks the role file matches the diff. |
| **patterns.md** | Stays as the *how* (mechanics). Roles catalog is the *what* (domain shape). They reference each other but don't overlap. |

### CRC-style discovery within a batch

When I (Architect) introduce a new aggregate or service in an ADR, I write the role file as a CRC card sketch and reason about it before locking the ADR:

1. Name the role.
2. State its responsibility in one sentence.
3. List candidate collaborators.
4. Ask: "What does this role NOT do?" Write the answers under `Does NOT know`.
5. Walk a key scenario through the role and check the collaborator list is sufficient.

If a scenario forces the role to know something on the `Does NOT know` list, either the responsibility is wrong or a new collaborator is needed. This catches design smells early.

### Reviewer enforcement

- Every new aggregate, value object, domain service, repository interface, or adapter interface introduced in code **must** have a corresponding role file. PR fails review otherwise.
- A handler may not directly depend on more than ~5 collaborators. If it does, the handler is doing too much — split via either a domain service or by collapsing collaborators behind a smaller interface.
- An entity that grows methods unrelated to its core responsibility is flagged. Example: `Order.IssueInvoice()` violates `Order`'s responsibility (Order = capture intent; Invoice = legal record). The right design is `InvoiceService.IssueFor(order)`.

### What RDD does NOT change

- **Patterns catalog stays authoritative for mechanics.** RDD doesn't change how we write `Command`/`Validator`/`Handler` or how we use `BusinessResult`.
- **CQRS still applies.** Commands and queries still drive the use-case layer. Roles describe the objects those use cases manipulate.
- **DDD aggregate boundaries** remain. RDD is compatible with aggregate-root thinking — an aggregate root is a role with a particular kind of responsibility (consistency boundary for a set of child entities).
- **Adapter pattern unchanged.** Adapter interfaces (`IPaymentProvider`) are roles with `kind: adapter`. Implementations don't get role files; only the interface does.

## Alternatives considered

- **Pure DDD (without explicit RDD vocabulary)** — rejected. DDD's "aggregate" / "entity" / "value object" vocabulary is necessary but not sufficient. It tells us how to *bound* objects but doesn't force us to name responsibilities or anti-responsibilities. The combination is stronger.
- **Pure CQRS without domain modeling** — rejected. The Cleansia approach succeeded *because* the user had implicit RDD intuition. Without making it explicit, multi-agent implementation will produce inconsistent designs.
- **Event Storming as the discovery technique** — interesting but synchronous-workshop-shaped. Doesn't fit our async, in-repo discovery process. RDD's CRC-card style produces similar outputs through asynchronous writing.
- **No design discipline beyond "follow patterns.md"** — rejected. The patterns catalog ensures consistent *mechanics*; it doesn't ensure consistent *modeling*.

## Consequences

### Positive
- **Single page per role** makes the domain model legible to anyone — including a sub-agent invoked fresh with no prior context.
- **`Does NOT know` lists prevent scope creep** at design time, which is much cheaper than catching it in code review.
- **New developers (or agents) can write a feature** by reading two or three role files and the relevant ADR, instead of grepping the codebase to infer responsibilities.
- **Refactoring becomes easier**: when a responsibility moves, the role file moves with it; the diff is visible.

### Negative
- **More documentation overhead.** Mitigated: role files are ~1 page; they pay for themselves the first time someone asks "what owns invoice numbering?" and gets a one-paragraph answer.
- **Risk of role-file rot** if developers don't update them when changing responsibilities. Mitigated: Reviewer enforcement; PR that changes a handler's collaborators must update the role file.
- **Some roles are obvious** (e.g. `Money` value object) and feel over-documented. Acceptable: the consistency value of having every role described outweighs the per-role cost.

## Compliance / verification

- Reviewer checklist: every new aggregate / value object / domain service / repository interface / adapter interface has a role file under `docs/architecture/roles/`.
- Reviewer checklist: handlers depend on at most ~5 collaborators. More = either redesign or escalate to Architect.
- Architect: ADRs that introduce a new role link the role file by relative path.
- BA: user stories include a "Roles in play" section listing roles the story uses (and creating new role files if needed).

## Related
- Patterns: §A.1 Layering, §A.2 Feature-folder layout, §A.9 Repository pattern, §A.15 Provider adapter
- ADR 0001 (layering — RDD lives within the layered architecture)
- ADR 0002 (CQRS — roles are the objects commands/queries manipulate)
