# Discovery protocol (Phase 1)

Discovery turns the user's intent into a frozen backlog. Run by **BA**, with **Architect** and **PM** in support.

## Goals

By the end of discovery, the following are written and signed off by the user:

1. `docs/personas.md` — who the customer, maker, and admin actually are
2. `docs/glossary.md` — domain terms (order, packet, payout batch, payout, fee invoice, etc.)
3. `docs/user-stories/<persona>/*.md` — every user-facing capability as a story with AC
4. `docs/adr/0001..NNNN-*.md` — every architectural decision with lasting impact
5. `docs/architecture/overview.md`, `extension-points.md`, `money.md`, `multi-country.md`
6. `docs/tickets/T-NNNN-*.md` — sized, sequenced backlog with dependencies

## Sequence

### Step 1 — Personas & glossary
BA drafts personas and glossary from the MVP spec + user input. Confirm with user.

### Step 2 — User stories per persona
For each persona, BA enumerates capabilities and writes one story per capability using the template. AC in Given/When/Then. Out-of-scope list is mandatory.

### Step 3 — Non-functional requirements
Capture in `docs/architecture/overview.md`: performance budgets, scale assumptions, availability target, observability, accessibility, browser/device support.

### Step 4 — Architectural decisions
Architect drafts ADRs for every decision with lasting impact:

- Money representation (integer minor units? currency awareness? rounding?)
- Multi-country abstraction (country as table column? schema? config?)
- Payment provider adapter pattern (so Comgate is one of N)
- Shipping provider adapter pattern (Zásilkovna is one of N)
- Tax/VAT regime abstraction
- Address model (CZ-specific fields vs. structured international)
- File storage layout and access control
- Auth provider lock-in (Supabase Auth vs. adapter)
- Email provider abstraction
- Locale/i18n strategy
- Order numbering across countries (collision-free)
- Invoice numbering across countries (compliance)
- Audit trail / event log strategy
- RLS strategy (per-country, per-tenant)
- Background job strategy (Vercel Cron vs. queue)
- Realtime updates (Supabase Realtime vs. polling)
- Error tracking / observability (Sentry? logs?)
- Testing strategy (unit, integration, E2E, manual)

### Step 5 — Backlog
PM converts stories into tickets, sequences them by dependency, sizes them (S/M/L), and writes the dependency map.

### Step 6 — Sign-off
User reviews the backlog, ADRs, and stories. Once signed off, autonomous build can start.

## Interview style

- BA batches questions — never one-off
- Questions are concrete and decision-forcing, not open-ended
- BA proposes a default with rationale; user accepts or redirects
- After every batch, BA updates the relevant files immediately
