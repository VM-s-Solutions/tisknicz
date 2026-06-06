# Way of working

One-page tour. If you read nothing else, read this. Then read [CLAUDE.md](../CLAUDE.md).

This page is the human-facing entry. It does not redefine anything — it points you at the canonical sources and shows how a request becomes shipped code.

---

## The one-screen picture

```
  user intent
      │
      ▼
  /plan <intent>            ← shape it (no code; PM + architect + ba)
      │
      ▼
  /feature <intent>         ← cut a ticket (PM owns)
      │
      ▼
  T-NNNN-<slug>.md          ← AC, scope, test plan ref, status log
      │
      ├── ADR-NNNN if architectural choice needed   (architect)
      └── US-<persona>-NNNN if user-facing behavior (ba)
      │
      ▼
  /execute T-NNNN
      │
      │   implementer        reviewer
      │   ──────────────     ───────────────
      │   dotnet-db          draft review while
      │   dotnet-backend     state == in_progress
      │   frontend           (catches drift early)
      │   l10n               final review at PR open
      │
      ├── /sync              ← if backend contract changed: NSwag regen
      │                       same PR, no manual edits to lib/api-client/
      ▼
  PR opened → Gate 1..6 (see docs/process/quality-gates.md)
      │
      ├── qa runs test plan against preview
      ├── reviewer signs Gate 1, 2, 5
      └── secops signs Gate 3 if security-touching
      │
      ▼
  user merges to master
      │
      ▼
  /execute next             ← PM picks the next ready ticket
```

The hand-offs are the slash commands. The agents are the workers. The ticket file is the contract.

---

## The team

Eleven agents. Each has a charter in [.claude/agents/](../.claude/agents/) and a CRC card in [docs/architecture/roles/](./architecture/roles/).

| Agent | Role |
|---|---|
| [pm](../.claude/agents/pm.md) | Owns the backlog. Picks the next ticket, expands `draft → ready`, updates [INDEX.md](./tickets/INDEX.md) and [status/sprint-N.md](./status/) on every state change. |
| [architect](../.claude/agents/architect.md) | Writes ADRs. Guards [patterns.md](./architecture/patterns.md) and the extension points. Engaged when a ticket touches an unprecedented choice. |
| [ba](../.claude/agents/ba.md) | Owns user stories. Refines AC for ambiguity. Engaged on user-facing behavior. |
| [dotnet-db](../.claude/agents/dotnet-db.md) | EF Core migrations, entity configurations, repositories, query filters. First implementer when schema moves. |
| [dotnet-backend](../.claude/agents/dotnet-backend.md) | CQRS features (Command/Query, Validator, Handler), controllers, adapters in `Infra.Clients/`. Regenerates the OpenAPI surface when the contract changes. |
| [frontend](../.claude/agents/frontend.md) | Next.js App Router pages + components. Calls the backend via `lib/api-client/` + `apiFetch`. Never reaches a database. |
| [l10n](../.claude/agents/l10n.md) | `lib/i18n/cs-CZ` keys for every new `BusinessErrorMessage` code and user-facing string. Czech-only at launch. |
| [qa](../.claude/agents/qa.md) | Writes the test plan while implementation is in flight. Executes against the preview environment before merge. |
| [reviewer](../.claude/agents/reviewer.md) | Runs the Gate 1..6 check against [docs/review/checklist.md](./review/checklist.md). Parallel draft review during `in_progress`; final review at PR open. |
| [secops](../.claude/agents/secops.md) | Gate 3. Auth, webhooks, file upload, secrets, PII, money columns. Mandatory signer on security-touching tickets. |
| optimizer | New 11th seat. Reads merged diffs and PR conversations for cross-ticket cleanup candidates — duplicated helpers, drifted patterns, missing index hints, slow query candidates. Files cleanup tickets back to PM. Never blocks a PR. |

Routing rules — which agent owns which ticket — live in [docs/process/routing.md](./process/routing.md).

---

## How a request becomes shipped code

1. **You state an intent.** Either as free text ("makers should see a tax summary on the dashboard") or as `/plan <intent>` if you want PM + architect + ba to shape it without writing code first.
2. **PM cuts a ticket.** `/feature <intent>` produces a `T-NNNN-<slug>.md` file using [docs/tickets/template.md](./tickets/template.md), wires it into [INDEX.md](./tickets/INDEX.md), and routes it. If the choice is architectural, architect drafts an ADR in [docs/adr/](./adr/) first. If the behavior is user-facing, ba drafts a US in [docs/user-stories/](./user-stories/).
3. **You run `/execute T-NNNN`.** PM picks the next `ready` ticket whose dependencies are `done`, moves it to `in_progress`, and assigns the implementer chain per [ticket-lifecycle.md](./process/ticket-lifecycle.md): dotnet-db → dotnet-backend → frontend → l10n. qa starts the test plan in parallel.
4. **Reviewer runs in parallel.** A draft review fires while state is `in_progress` so contract drift, missing `BusinessErrorMessage` codes, and pipeline-behavior misuse get caught before PR open. A second, final review fires at PR open against [docs/review/checklist.md](./review/checklist.md).
5. **`/sync` if the contract moved.** Any change to a controller signature, request DTO, response DTO, or error code regenerates `frontend/src/lib/api-client/` via NSwag in the same PR. The pre-commit hook from T-0013 blocks manual edits to that folder.
6. **PR opens, gates run.** [docs/process/quality-gates.md](./process/quality-gates.md) is the rulebook. Reviewer signs Gate 1 (self-check), Gate 2 (AC traceability), Gate 5 (TDD for pure logic on T-0067+; T-0001..T-0066 grandfathered). secops signs Gate 3 if security-touching. qa signs Gate 4 by executing the test plan against the preview environment.
7. **You merge.** Master moves forward. PM closes the ticket, updates the sprint status doc, and the optimizer scans the diff for cleanup candidates. Then `/execute next` and we go again.

---

## Quality bar

> Would I run this unattended for a Czech marketplace handling real money, real customers, with JVM YORE s.r.o.'s name on the invoice?

That is the bar. It is not "does it compile" or "does the happy path work." Money is `long` minor units with explicit `Currency`. State transitions are atomic. Webhooks are idempotent and signature-verified. Every protected endpoint has `[Authorize]` or middleware. Every user-facing string has a Czech i18n key. Every monetary column ends `_minor` and is paired with `currency CHAR(3) NOT NULL`.

The reviewer enforces the bar via the self-check in [CLAUDE.md §Self-check before declaring a task done](../CLAUDE.md#self-check-before-declaring-a-task-done) — backend, frontend, and cross-stack sections. If any item fails, reviewer rejects. Hygiene is on the implementer, not on you.

Live becomes expensive fast. Once we cut over, schema migrations cost downtime, contract changes cost a regen-and-deploy of every host, and a money-rounding mistake costs trust. Bias every decision toward long-term flexibility now.

---

## First job — current state

We are in **Phase 4 — orders + payments + invoicing**. Phases 1 (foundation), 2 (identity), and 3 (catalog) are merged. See [docs/tickets/INDEX.md](./tickets/INDEX.md) for the full manifest and sprint plan.

Open Phase 4 work, in dependency order:

- **T-0060** Order entity + state machine (`Pending → Paid → InProduction → Shipped → Delivered`, terminal `Cancelled` / `Refunded`)
- **T-0061** OrderPricing domain service (line totals, VAT basis points, half-up rounding, CZK haléř strip on display)
- **T-0062** OrderNumberGenerator rescope on top of `NumberingSequence` from T-0007
- **T-0063** `CreateOrder` command (cart → order, inventory check, customer + maker snapshots, idempotency key)
- **T-0064** Order attachments (customer artwork upload through `IBlobStorageClient` from T-0042)
- **T-0065** Comgate payment provider adapter under `Infra.Clients/Comgate/`
- **T-0066** Comgate webhook (origin verify, signature verify, idempotent by `provider_ref`, single-transaction state transition)
- **T-0067** Mark-order-paid via outbox (first TDD-enforced ticket per Gate 5)

After Phase 4 closes, Phase 5 picks up post-order: invoicing, payouts, shipping label generation, dispute flow.

---

## Day-to-day driving

At session start, one of these:

- **`/feature <intent>`** — kicks PM to cut a new ticket. Use when you have a fresh need.
- **`continue with next ticket`** or **`/execute next`** — PM picks the next `ready` ticket whose dependencies are `done` and runs it.
- **`/plan <intent>`** — shape something without committing to code. Useful for unprecedented choices that probably want an ADR before a ticket.

Mid-session:

- **`/sync`** if the backend contract moved and the frontend client needs regenerating.
- **`/audit`** to scan the repo for pattern drift, dead code, missing `BusinessErrorMessage` keys, missing i18n parity. (Landed as a command; first run deferred until Phase 4 + 5 ship.)
- **`/review T-NNNN`** to fire the reviewer manually against a PR.

End of day:

- PM updates [docs/status/](./status/) with what moved.
- Open questions live in [docs/questions/open.md](./docs/questions/open.md) — never block a ticket on an unanswered question without parking it there.

The full command set is in [.claude/commands/](../.claude/commands/). The canonical patterns are in [docs/architecture/patterns.md](./architecture/patterns.md). The agent charters are in [.claude/agents/](../.claude/agents/). Everything routes back to [CLAUDE.md](../CLAUDE.md).
