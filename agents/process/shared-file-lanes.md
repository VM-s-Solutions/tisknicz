# Shared-File Lanes — the serialization list

When a batch fans out in parallel, tickets that touch the **same shared file** must be **serialized**
into a single lane (one writer at a time). This file is the **maintained data list** of those files.
The PM validates every parallel batch's lane assignments against this list **before dispatch**; a
batch plan that puts two concurrent writers on any cluster below is wrong by construction.

Why this is a file and not folklore: a `git restore` on a shared file to "clean contamination" during
a parallel batch silently wipes a co-writer's committed deliverable, and the combined-tree re-verify
only catches it after the fact — if it catches it at all. The structural fix is this list + the
serialization rule + the restore ban (see [quality-gates.md](../../docs/process/quality-gates.md)
§"Serialize shared-file lanes" and every dev charter's constraints in
[.claude/agents/](../../.claude/agents/)).

## The clusters

| Cluster | Files | Why it collides |
|---|---|---|
| Consistency baseline | `docs/audits/consistency-violations.md` | Auto-generated grandfather list (`scripts/check-consistency.mjs --update-baseline`); every debt-codification / canonicalization ticket shrinks or annotates rows. Two concurrent writers (or one blanket regenerate) destroy each other's edits. Shrink, never grow — one writer at a time. |
| Backlog manifest | `docs/tickets/INDEX.md` | One row per ticket in one table — every ticket in a batch wants to update its own row (and its sprint block) at close-out. PM owns it; concurrent state-change writes collide. |
| Czech message catalog | `frontend/src/lib/i18n/cs-CZ.ts` | The **single** source of user-visible copy (Makables ships Czech-only per [CLAUDE.md](../../CLAUDE.md)). Every `frontend` / `l10n` ticket that adds a string appends here; two tickets in one batch collide on the same object literal. One catalog → one lane for the whole batch. Every `BusinessErrorMessage.*` code needs a parallel key here (parity is `l10n`-enforced). |
| Error-code catalog | `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` | Every backend ticket adding an error key appends here. Two concurrent appenders collide; the paired `cs-CZ.ts` key (above) must land in the same PR. Serialize the backend appenders and keep each on the same lane as its catalog edit. |
| Host-audience cluster (3 files — they move together) | `backend/src/Makables.Config/Extensions/AddMakablesAuth.cs` (`AcceptedAudiencesFor`) + `backend/src/Makables.Core.Domain/Identity/MakablesHosts.cs` + `backend/src/Makables.Core.Domain/Identity/MakablesAudiences.cs` | A new host surface or a change to which JWT audience a host accepts needs all three in ONE change, or a host boots with the wrong audience table and a customer JWT replays against the maker API (the exact ADR 0012 invariant). `AcceptedAudiencesFor` is the runtime source of truth; the constants back it. Exactly one cluster editor per pass. |
| Project guardrails | `CLAUDE.md` (repo root) | Read by every agent at spawn; a mid-batch edit changes the rules under running lanes. Owner/orchestrator-gated — never a ticket-lane edit. |

## Other observed serialization clusters (same rule, narrower blast radius)

- The **admin shell** — `frontend/src/app/(admin)/shell-nav.tsx` (`LIVE_NAV` / `PENDING_NAV`) plus the
  route folders it links under `frontend/src/app/(admin)/dashboard/admin/`: every new admin section
  adds exactly one nav entry (the T-0118a → T-0118c → T-0126 → T-0127 → T-0140 chain). Two admin
  features in one batch both edit `LIVE_NAV` — serialize them.
- **Per-host `Program.cs`** — the four `backend/src/Makables.Web.{Customer,Maker,Admin,Public}/Program.cs`
  files are a flat list of `AddMakables*()` calls (ADR 0008). A cross-cutting registration (new
  behavior, new middleware) that must land in all four is one change on one lane, not four parallel
  edits racing the same insertion point. Note these are **independent per host** — two tickets each
  touching a *different* host's `Program.cs` do not collide.
- **DI wiring** — `backend/src/Makables.Config/Extensions/*.cs` (`AddMakablesInfrastructure`,
  `AddMakablesClients`, `AddMakablesMediator`, …): a new provider adapter or service registered in the
  same extension method is a serial lane with any other ticket editing that method.
- The **NSwag spec-hash file** — `frontend/src/lib/api-client/.spec-hashes.json`: any contract change
  regenerates it. A bundle touching Customer + Maker + Admin + Public controllers regenerates all four
  clients and this one hash file — one lane, one regen pass, verified with `npm run check:api` before
  PR-open (the contract-parity Gate 6 rule; a bundle that regens only the primary host fails late).

## The rules

1. **The PM validates lane assignments against this list** before dispatching a parallel batch: any
   two tickets touching the same cluster are serialized into one lane (or handed to one agent, in
   sequence). [routing.md](../../docs/process/routing.md) sequencing rules bind this — schema before
   code, `l10n` parallels `frontend` on the same ticket, and NSwag regen covers every host whose
   controllers changed.
2. **Parallel agents edit only their own hunks** when adjacency on a shared file is unavoidable —
   never a rewrite, never a reformat. Append your row/key/const; leave the rest byte-for-byte.
3. **NEVER `git restore` / `git checkout --` / wholesale-revert a shared file** in a parallel batch.
   An agent that believes a shared file is contaminated **reports it to the PM** (a note in its
   ticket); it does not revert. This rule is also in every dev charter's constraints in
   [.claude/agents/](../../.claude/agents/).
4. **Maintain the list.** A collision on a file not listed here is two bugs: the collision, and the
   missing row. The fix adds the row in the same change.

## Cross-references

- Who picks up which ticket and the sequencing rules: [routing.md](../../docs/process/routing.md)
- Gates that fire at PR-open (incl. the restore ban and contract parity): [quality-gates.md](../../docs/process/quality-gates.md)
- Ticket states and Definition of Ready: [ticket-lifecycle.md](../../docs/process/ticket-lifecycle.md)
- How agents hand off (artifacts, not chat): [communication.md](../../docs/process/communication.md)
- Agent charters: [.claude/agents/](../../.claude/agents/){architect, ba, dotnet-backend, dotnet-db, frontend, l10n, optimizer, pm, qa, reviewer, secops}.md
- Backlog manifest owned by PM: [docs/tickets/INDEX.md](../../docs/tickets/INDEX.md)
- Open blocking questions: [docs/questions/open.md](../../docs/questions/open.md)
