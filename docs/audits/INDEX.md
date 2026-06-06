# Audits index

One-page index for every `/audit` run and recurring sweep against the Makables codebase. This file is the source of draft tickets — findings here graduate into [docs/tickets/INDEX.md](../tickets/INDEX.md) when they are ready to schedule.

> **Current state — no audits run yet.** Per user decision, the first `/audit` run is **deferred until Phase 4 (orders) and Phase 5 (post-order) ship**. The `/audit` slash command lands ahead of that date so the harness is ready, but `optimizer`, `reviewer`, `secops`, and `ba` will not produce audit files until then. Do not seed this index with speculative findings.

---

## Purpose

- Track findings from on-demand `/audit` runs and from recurring sweeps (security, perf, conventions, gaps).
- Be the **single source of draft tickets** that originate from auditing rather than from user stories. Every finding either becomes a ticket or is closed with a recorded reason.
- Give the team a one-page view of what is open, what is in flight, and what has been resolved — without bloating [docs/tickets/INDEX.md](../tickets/INDEX.md) with raw observations.

Audits are not a substitute for the per-PR [reviewer checklist](../review/checklist.md). They are the periodic, codebase-wide pass that catches drift the per-PR gate cannot see.

---

## Layout

One file per **(subsystem × dimension)** pair. Naming: `<subsystem>-<dimension>.md` (kebab-case, both halves singular).

Examples:

- `orders-perf.md` — optimizer sweep over the orders subsystem
- `identity-security.md` — secops sweep over identity (auth, users, makers, tokens)
- `catalog-conventions.md` — reviewer sweep over catalog (products, variants, media)
- `platform-gaps.md` — ba sweep over platform-wide cross-cutting concerns

Every file follows [docs/audits/_template.md](./_template.md). Do not invent ad-hoc layouts — if the template is missing a field you need, amend the template and back-port the existing files in the same PR.

A finding lives **only** in its audit file until it is promoted to a ticket. Once promoted, the audit file keeps the finding but flips its status to `→ T-NNNN` so the trail is not lost.

---

## Subsystems

The four audit subsystems mirror the per-audience hosts and the business pillars they serve. They are intentionally coarse — finer-grained tagging happens inside each file.

| Subsystem | Covers | Primary hosts |
|---|---|---|
| **identity** | Users, makers, sessions, passwords, magic links, email confirmation, OAuth, JWT, refresh tokens, RBAC | Web.Customer, Web.Maker, Web.Admin |
| **catalog** | Products, variants, media, search, browse, taxonomy, maker storefronts | Web.Customer, Web.Maker, Web.Public |
| **orders** | Cart, checkout, payments, shipping, order lifecycle, webhooks, invoices, payouts, refunds | Web.Customer, Web.Maker, Functions |
| **platform** | Cross-cutting: outbox, audit log, i18n, observability, deployment, country configuration, NSwag contract, shared infra | all hosts |

A finding that touches more than one subsystem belongs in the **most upstream** one — the subsystem whose change would propagate to the others. If unclear, file it under `platform`.

---

## Dimensions

Each dimension has an **owning agent**. That agent runs the sweep, writes the file, and is the default assignee on every finding until it is promoted to a ticket or closed.

| Dimension | Owner agent | What it looks for |
|---|---|---|
| **gaps** | [ba](../../.claude/agents/ba.md) | Missing user stories, missing acceptance criteria, missing personas, requirements that the code silently does not satisfy |
| **conventions** | [reviewer](../../.claude/agents/reviewer.md) | Drift from [docs/architecture/patterns.md](../architecture/patterns.md) (A.1–A.21 backend, B.1–B.19 frontend), CLAUDE.md violations that escaped per-PR review, dead code, naming, layering |
| **security** | [secops](../../.claude/agents/secops.md) | AuthN/AuthZ holes, missing `[Authorize]`, audience confusion, secret leakage, webhook signature gaps, RLS-equivalent gaps in EF query filters, input validation, rate limiting |
| **perf** | [optimizer](../../.claude/agents/optimizer.md) | N+1 queries, missing indexes on WHERE/ORDER BY/JOIN columns, missing `.AsNoTracking()` on read-only queries, unbounded list endpoints, client bundle bloat, render-blocking patterns |

The architect, dotnet-backend, dotnet-db, frontend, l10n, and qa agents may **contribute** findings to any file, but they do not own a dimension. If a contributor disagrees with the owner's call on a finding, escalate via [docs/questions/open.md](../questions/open.md) — never edit the finding past the owner.

---

## Severity scale

Severity is set by the owning agent at file time and is **not** negotiable by the implementer. If severity is wrong, the owner re-grades it; the implementer does not downgrade their own ticket.

| Severity | Meaning | SLA from open to ticket-draft |
|---|---|---|
| **BLOCKER** | Live data loss, security breach, payment correctness, or a hard production outage is happening or imminent. Stops the sprint. | Same day |
| **High** | Real user impact in production within the next release. Auth bypass with mitigations, money math drift inside tolerance, perf regression that violates a stated budget. | 2 business days |
| **Medium** | Real but contained: degrades a flow, breaks a non-critical path, or violates an architectural rule with no immediate user impact. | Within the current phase |
| **Low** | Quality / hygiene / minor pattern drift. Worth fixing, no schedule pressure. | Next available sprint |
| **Nit** | Style, naming, doc polish. Batched into housekeeping tickets, never tracked individually. | When convenient |

A `BLOCKER` finding bypasses the normal lifecycle and goes straight to a ticket the same day it is filed. The audit file still records it for the audit trail.

Money math, auth, and webhook signature findings start at **High** by default and may only be downgraded with an explicit defense in the audit file's `## Alternatives Considered` block.

---

## Lifecycle

Every finding moves through these states. The owning agent advances the state; nobody else does.

1. **open finding** — written into the audit file with severity, evidence (file paths + line ranges or commit SHAs), and a one-sentence proposed fix. No ticket yet.
2. **ticket draft** — finding is judged real and actionable. Owner drafts the row for [docs/tickets/INDEX.md](../tickets/INDEX.md) and links it in the audit file as `→ draft T-NNNN`.
3. **merged into backlog** — PM has accepted the row into [docs/tickets/INDEX.md](../tickets/INDEX.md) and assigned a phase. The audit file row now reads `→ T-NNNN (backlog)`.
4. **resolved** — the ticket is `done` and merged. The audit file row reads `→ T-NNNN (done <YYYY-MM-DD>)`. The finding stays in the file forever as historical evidence.

A finding may also be **closed without a ticket**. The owner must record the reason inline (`closed: duplicate of finding X`, `closed: wontfix, see ADR-NNNN`, `closed: invalid, evidence misread`). Silent deletion of findings is a process violation — reviewer rejects PRs that do it.

The audit file itself is never archived. Subsystem × dimension files are append-only across the lifetime of the project.

---

## Index of audit files

> Empty. First entries land after Phase 4 + Phase 5 ship and the first `/audit` runs against the live codebase.

| File | Subsystem | Dimension | Owner | Last run | Open / Drafted / Done |
|---|---|---|---|---|---|
| _(none yet)_ | | | | | |

When the first audit runs, add one row per file. Keep the row updated on every subsequent run — do not add a row per run.

---

## Operating notes

- **Cadence:** target is once per phase boundary (after Phase 4, after Phase 5, after Phase 6) plus on-demand `/audit` runs triggered by PM. Do not run more often during build — the team is the audit during build.
- **Scope creep:** if an audit run keeps surfacing the same root cause across files, stop and open an ADR instead of filing five tickets. The ADR is the real fix; the tickets are downstream.
- **Cross-link discipline:** every finding cites its evidence with a repo-relative path (e.g. `backend/src/Core.AppServices/Features/Orders/PlaceOrder.cs:42`) and the commit SHA the audit ran against. No screenshots, no paraphrased code.
- **Defense in artifact:** when the owner makes a judgement call (severity, in-scope vs. out-of-scope, ticket vs. close), record the alternatives considered and the defense inline. Per the project's deliberation rule, the user is the challenger — make the reasoning visible so they can challenge it.

---

## Alternatives Considered

- **Single flat audit log file.** Rejected: would not scale past Phase 4. Per (subsystem × dimension) gives every owner agent a stable home and lets findings accumulate in context.
- **Findings tracked directly in [docs/tickets/INDEX.md](../tickets/INDEX.md).** Rejected: pollutes the backlog with raw observations that may never become tickets, and loses the audit-trail value of historical evidence.
- **One file per audit run (dated).** Rejected: same finding gets re-discovered every quarter and the duplication is invisible. The (subsystem × dimension) layout forces de-dup at file time.
- **Defer the index file until Phase 4+5 ship.** Rejected by user: the `/audit` slash command lands now so the harness is ready; this index gives the command somewhere to write. The first **run** is deferred, not the scaffolding.

## Defense

This file exists ahead of the first `/audit` run because the slash command, the owner-agent charters, and the lifecycle have to be in place before findings can be filed coherently. Authoring the index after the first run would mean the first run files findings into a structure that does not exist yet — guaranteeing rework. The cost of this file sitting empty for two phases is one page of markdown; the cost of filing findings into an unspecified structure is every owner agent inventing their own.
