# /team — Delegate to the Makables agent team

Hand a request to the agent team. This is the **primary** way to work: describe
what you want in plain language and the PM coordinates the specialists
end-to-end. For a tiny, well-scoped single-layer change you can still reach for a
narrower command, but anything cross-stack or non-trivial goes through `/team`.

## Usage
```
/team <describe what you want, in plain language>
```

## What it does
You are the **Orchestrator**. On `/team`:

1. Read [agents/WAY-OF-WORKING.md](../../agents/WAY-OF-WORKING.md) and
   [agents/process/routing.md](../../agents/process/routing.md) so you operate the
   team correctly.
2. Invoke the **PM** (`subagent_type: "pm"`) with the user's request. The PM will:
   - dedup against [docs/tickets/INDEX.md](../../docs/tickets/INDEX.md) and open audits;
   - convene a **defense panel** (`ba` for a user story, `architect` for an ADR)
     before ticketing anything with a real decision, per
     [agents/process/deliberation.md](../../agents/process/deliberation.md) — pure
     mechanical work skips the panel with a "no-decision" note;
   - turn the request into one or more tickets in
     [docs/tickets/](../../docs/tickets/) (+ a row in `INDEX.md`), each passing the
     Definition of Ready in
     [agents/process/ticket-lifecycle.md](../../agents/process/ticket-lifecycle.md);
   - route to specialists per
     [agents/process/routing.md](../../agents/process/routing.md) — contract first
     (`architect → dotnet-db → dotnet-backend`), then `frontend` / `l10n` fan out;
   - **spawn a `reviewer` instance in parallel with every developer instance**;
   - run the gates (`secops` if `security_touching`, `optimizer` on hot paths,
     `qa`, and the mechanical Gate) per
     [agents/process/quality-gates.md](../../agents/process/quality-gates.md);
   - flag any owner-only `manual_steps` (EF migration apply, NSwag client regen)
     and hold dependent work;
   - update the current sprint status.
3. Relay the PM's outcome to the owner: tickets created, what shipped, what needs
   the owner (questions in [docs/questions/open.md](../../docs/questions/open.md),
   manual steps).

## Rules
- Communication is artifact-based — everything lands in `docs/` (tickets, ADRs,
  questions, review notes). No work happens "verbally".
- Do not run EF migrations or regenerate the NSwag client — flag them as
  `manual_steps` for the owner (per CLAUDE.md).
- Do not commit or push unless the owner explicitly asks.
- The contract is the seam: any backend contract change regenerates the frontend
  client in the same PR (CI verifies parity).

## Related commands
- `/plan` — decompose a request into ready tickets, no code.
- `/execute` — run an already-ready ticket end-to-end to merge.
- `/feature` — turn intent into a ticket-to-PR cycle.
- `/review` — reviewer pass over the current diff or a PR.
- `/audit` — fan out analysts/reviewers over subsystems × dimensions.
- `/sync` — detect a stale NSwag client and produce regen instructions.
