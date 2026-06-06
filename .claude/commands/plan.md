# /plan — decompose a request into tickets without writing code

Planning-only mode. You read, you think, you write user stories / ADRs / tickets — you do **not** touch implementation files (no `backend/src/**`, no `frontend/src/**` except the i18n catalog if l10n is invoked). The goal is a sequenced, sized, dependency-mapped backlog the user can sign off before any branch is cut.

If the request is fuzzy, this command pulls `ba` in first. If it crosses a seam without ADR coverage, it pulls `architect` in. Output ends with an explicit handoff line — the user decides whether to run `/feature` or `/execute` next.

## When to use

- The user describes an outcome ("makers can refuse a quote and counter-offer") and you need to turn it into tickets.
- A new capability lands mid-sprint and you need to know where it fits in the dependency graph.
- A ticket is too big (sized **L** but won't split obviously) and needs decomposition.
- You want to dry-run scope before committing — see what stories, ADRs, and tickets it implies, and what open questions block readiness.
- The user asks "what would it take to ship X?" — `/plan` answers in artifacts, not prose.

**Do not use `/plan`** when the work is a single well-scoped ticket already in `ready` state — go straight to `/execute`. Do not use it for hotfixes or trivial copy edits.

## Steps

1. **Load the canon.** Read, in order:
   - `CLAUDE.md` (root) — rules in force.
   - `docs/architecture/patterns.md` — pattern catalog A.1–A.21 (backend) and B.1–B.19 (frontend). Every ticket scope must map to existing patterns or flag a new one for `architect`.
   - `docs/architecture/overview.md` and `docs/architecture/extension-points.md` — system shape and the seams where variation is expected.
   - `docs/adr/` — scan titles; deep-read any ADR whose subject the request touches (payments → 0008, shipping → adapter ADRs, money → 0003, country → 0004, auth → 0012, NSwag → 0022, RDD → 0015, stack pivot → 0007).
   - `docs/tickets/INDEX.md` — current backlog state, phase assignments, dependencies.
   - `docs/questions/open.md` — known open questions that may block readiness.

2. **Restate the request in domain terms.** One paragraph in your response: what the user actually asked for, mapped to Makables vocabulary (order, packet, payout batch, fee invoice, maker, customer, admin, country). Flag any term that doesn't appear in `docs/glossary.md` as a question for `ba`.

3. **Invoke `ba` if the request is fuzzy or user-facing.** Trigger conditions:
   - Outcome described, capability undefined.
   - Persona unclear (customer? maker? admin? all three?).
   - Acceptance criteria not derivable from the request.
   - New domain term appears.

   `ba` produces or updates `docs/user-stories/<persona>/US-<persona>-NNNN-*.md` per `docs/user-stories/template.md`: narrative, **Roles in play** (per ADR 0015), AC in Given/When/Then, out-of-scope list, related ADR/ticket links. Czech terms go to `docs/glossary.md`. Unanswered questions go to `docs/questions/open.md` with a defensible default noted.

4. **Invoke `architect` if the request touches an extension point or has no ADR coverage.** Trigger conditions:
   - New adapter seam (payment provider, shipping carrier, registry lookup, geocoder, email provider).
   - New cross-cutting concern (caching layer, rate limiting strategy, observability dimension).
   - Money / VAT / currency math beyond what `docs/architecture/money.md` covers.
   - Country variation beyond what `CountryConfiguration` currently exposes.
   - Pattern not in `docs/architecture/patterns.md`.

   `architect` produces a new ADR under `docs/adr/NNNN-<slug>.md` following `docs/adr/template.md`, and if RDD roles change, adds or updates `docs/architecture/roles/<role>.md`. Cheap-deliberation rule: capture rejected options under **## Alternatives Considered** and the load-bearing trade-offs under **## Defense**.

5. **Draft tickets per `docs/tickets/template.md`.** One ticket per shippable slice. Each ticket file lives at `docs/tickets/T-NNNN-<slug>.md` and carries frontmatter (id, title, status: `draft`, size, depends_on, blocks, user_stories, adrs, phase) and these sections: Context, Scope, Out of scope, Acceptance criteria (Given/When/Then, traced to US AC), Technical notes, Files touched (expected), Test plan reference, Status log.

   Sizing rule from `docs/process/ticket-lifecycle.md`:
   - **S** < 4h, single file domain, no new ADR.
   - **M** 4–16h, multi-file, may touch one ADR.
   - **L** > 16h, new ADR likely — **must split before `ready`**.

   Cross-stack rule: a typical feature flows `dotnet-db` (if schema) → `dotnet-backend` → NSwag regen → `frontend` → `qa` → `reviewer`/`secops`. Decide per ticket whether it is one cross-stack ticket (small contract, locked early) or split (large contract, parallel work, separate PRs each gated by the NSwag regen).

6. **Split every `L` ticket now.** Do not leave an `L` in the plan. Common split axes:
   - Schema + repository → its own `dotnet-db` ticket.
   - Backend feature (Command/Validator/Handler) → its own `dotnet-backend` ticket.
   - Controller + NSwag regen → bundle with backend ticket (atomic contract).
   - Frontend page + components → its own `frontend` ticket, depends on contract lock.
   - l10n keys → bundled with the frontend ticket that introduces them, unless the copy block is large enough to stand alone.

7. **Update `docs/tickets/INDEX.md`.** Add each new ticket as a row in the appropriate phase table with phase / size / state=`draft` / depends_on / stories / adrs columns. Keep tickets in dependency order within each phase.

8. **Run the Definition of Ready (DoR) checklist on every new ticket** (new tickets only — T-0001 through T-0067 are grandfathered):
   - [ ] AC items present, each in Given/When/Then, each verifiable.
   - [ ] AC traces back to at least one US AC or an explicit infrastructure rationale.
   - [ ] Scope and Out-of-scope both written.
   - [ ] Depends_on lists all blocking tickets; blocked tickets stay `draft`.
   - [ ] Size assigned (S / M / no L).
   - [ ] ADR coverage: every architectural choice points to an existing or new ADR.
   - [ ] Pattern coverage: every implementation touchpoint maps to a pattern in `docs/architecture/patterns.md`.
   - [ ] If pure logic (money math, VAT, numbering, state transitions): TDD-required note in Technical notes (T-0067+ hard rule per user-locked decision 3).
   - [ ] Files touched (expected) lists concrete paths.
   - [ ] Test plan reference path written (file may not exist yet; `qa` authors it during dev).
   - [ ] No open question blocks the ticket — if one does, link it and keep state `draft`.

   A ticket only transitions `draft → ready` after every box is checked. `/plan` may leave tickets in `draft` — promotion to `ready` happens when DoR clears, either inside `/plan` or in a follow-up PM pass.

9. **Report the plan back to the user.** Your final reply must include, in this order:
   - **Sequence** — the ordered list of new ticket IDs respecting dependencies.
   - **Parallelism** — which tickets can run concurrently once their dependencies are done (typically `dotnet-backend` and `frontend` after the contract is locked; `l10n` any time after AC is locked; `qa` test plan authoring during dev).
   - **Open questions** — every entry added or referenced in `docs/questions/open.md` during this run, with the defensible default `ba`/`architect` adopted.
   - **Manual steps** — anything outside agent tooling: secrets to provision, third-party accounts to register, Bicep/infra parameters to set, DNS to point, content the user owes (legal text, Czech copy decisions).
   - **DoR status per ticket** — which are `draft` only because a question is open, which would be `ready` after a trivial follow-up, which are already `ready`.

10. **End with the handoff line, verbatim:**

    > **ready? run `/feature` or `/execute`**

    Do not start implementing. Do not open a branch. Do not edit code files. `/plan` ends here.

## See also

- `docs/process/discovery.md` — the Phase 1 protocol this command operationalizes for ongoing work.
- `docs/process/ticket-lifecycle.md` — states, transitions, parallelism rules, sizing.
- `docs/process/quality-gates.md` — what `ready`, `in_review`, `qa`, and `done` actually require.
- `docs/process/communication.md` — how plan output is reported to the user.
- `docs/tickets/template.md` — ticket file shape.
- `docs/tickets/INDEX.md` — backlog manifest you update.
- `docs/user-stories/template.md` — story file shape `ba` uses.
- `docs/adr/template.md` — ADR file shape `architect` uses.
- `docs/architecture/patterns.md` — pattern catalog every ticket maps to.
- `docs/architecture/extension-points.md` — seams where new ADRs typically land.
- `docs/questions/open.md` — where unresolved decisions are parked.
- `.claude/agents/ba.md`, `.claude/agents/architect.md`, `.claude/agents/pm.md` — agent charters this command coordinates.
