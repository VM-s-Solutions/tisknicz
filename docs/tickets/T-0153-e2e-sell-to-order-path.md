---
id: T-0153
title: Complete the core marketplace path end-to-end — maker lists a product, it shows in the catalog, a customer orders it
status: ready
size: M
owner:
created: 2026-07-17
updated: 2026-07-17
depends_on: [T-0049, T-0046, T-0048, T-0084a, T-0084b, T-0085, T-0087b, T-0138, T-0152]
blocks: []
user_stories: [US-maker-0004, US-customer-0007, US-customer-0009, US-customer-0010, US-maker-0006]
adrs: [0016, 0017, 0022, 0023]
phase: 7
manual_steps: [deploy-trigger, vendor-account]
security_touching: false
layers: [frontend, dotnet-backend, secops]
---

# T-0153 — Complete the core marketplace path end-to-end

## Context

Every building block of the core marketplace loop has shipped as an individual
ticket — maker product CRUD (T-0049), public catalog + product detail
(T-0046/T-0048), checkout + payment + confirmation (T-0084a/b, T-0085), maker
order handling (T-0087a/b) — but the loop has **never been walked end-to-end
as one continuous user journey**, and the 2026-07-04 dev-web revision found
the dev backend App Services returning 503 (dopady §4 🔴 1: "web je jen
fasáda"). The website is not "complete" until a real maker can register, list
a product, see it appear in the public catalog, and a real customer can order
and pay for it — with every seam (auth, NSwag contract, cookies across hosts,
Comgate sandbox, Zásilkovna widget, emails) actually exercised together.
This ticket is the closing pass: revive the environment, walk the whole
journey, fix the small gaps it surfaces, and split anything bigger into
follow-up tickets.

## Scope

- **Revive the dev environment** (dopady §6 item 1): backend App Services
  (`public`/`customer`/`maker`) up and answering; DB migrated + seeded;
  frontend pointed at them (`NEXT_PUBLIC_API_*_BASE_URL`). Diagnose via App
  Service log stream per the T-0134 runbooks; T-0138 deploy fixes are the
  starting point.
- **Resolve the frontend↔backend cookie domain**: the session cookies (ADR
  0012) and the SSR cookie forwarding (patterns.md B.14) require the Next.js
  app and the API hosts to share a cookie-visible domain. On bare
  `*.azurewebsites.net` (public-suffix list) they cannot — decide and apply
  the dev-domain strategy (custom subdomains of one parent domain, e.g.
  `dev.makables.cz` + `api-*.dev.makables.cz`, or a frontend proxy). Without
  this, login works only on localhost.
- **Walk the maker journey**: register via `/register/maker` (ARES prefill) →
  confirm email → admin verifies maker → log in → create a product with
  images and price → product visible on `/katalog` and `/produkt/[id]`.
- **Walk the customer journey**: register → confirm email → log in → order
  that product (`/objednavka?productId=`, incl. Zásilkovna pickup point) →
  pay via Comgate sandbox → land on the confirmation page → order visible in
  `/dashboard/zakaznik/objednavky`.
- **Close the loop**: maker sees the paid order (Nové tab), accepts, ships
  (label download), customer confirms delivery; both invoice PDFs download;
  order emails arrive (outbox → SendGrid).
- **Fix in place** any small defect (wiring, config, copy, broken link, missing
  env var) the walk surfaces; **file a follow-up ticket** for anything larger
  than ~half a day and continue the walk around it.
- Record the pass/fail evidence per step in `docs/test-plans/T-0153-e2e-walk.md`
  (the T-0135 smoke checklist is the row template — this is its "run" for the
  core loop).

## Alternatives Considered

- **Automate the journey as a Playwright suite instead of a manual walk** —
  *rejected for this ticket: the environment itself is the unknown (503s,
  cookies, sandboxes); automation would be built against a broken target.
  A follow-up E2E-automation ticket is worth filing once the walk passes.*
- **Wait for the Stripe migration (T-0142) before verifying payments** —
  *rejected: Comgate is the wired, working provider today (ADR 0016) and the
  checkout/webhook seams it exercises (session creation, webhook idempotency,
  state machine) survive the provider swap unchanged. Verifying now
  de-risks everything except the provider adapter itself.*

## Out of scope

- Stripe Connect implementation (T-0142) and any payment-provider change.
- Cart, quote calculator, newsletter (T-0149–T-0151).
- Load (T-0132) and full 40-row smoke run (T-0135) — this ticket walks the
  single core loop deeply, not every surface.
- Production environment; this is dev/staging only.

## Acceptance criteria

- **AC-1** Given the dev environment, when any of the three API hosts is
  probed on `/api/v1/...`, then it answers (no 503/timeout) and the frontend
  catalog renders live data instead of "Server je momentálně nedostupný".
- **AC-2** Given a brand-new maker, when they register with a valid IČO,
  confirm email, are admin-verified, and create a product with at least one
  image, then that product appears in the public `/katalog` list and on its
  `/produkt/[id]` detail page without manual intervention.
- **AC-3** Given a brand-new customer, when they order that product and pay
  through the Comgate sandbox, then the order reaches `Paid` via the webhook
  (not the redirect), the confirmation page shows it, and it lists in
  `/dashboard/zakaznik/objednavky`.
- **AC-4** Given the paid order, when the maker accepts and ships it and the
  customer confirms delivery, then each transition succeeds from the
  dashboards, the label and both invoice PDFs download, and the lifecycle
  emails arrive.
- **AC-5** Given the full walk, when it completes, then
  `docs/test-plans/T-0153-e2e-walk.md` records per-step evidence, and every
  defect found is either fixed in this ticket's PR(s) or filed as a new
  ticket in `INDEX.md` — zero silent skips.
- **AC-6** Given a logged-in maker and customer (T-0152 chrome), when they
  navigate between public pages and their dashboards on the dev domain, then
  the session survives (cookie domain strategy works outside localhost).

## Technical notes

- Backend-down diagnosis order: App Service Log stream → container start
  errors (connection string / Key Vault refs from T-0138) → migration job.
- Comgate sandbox return URLs must be set per environment in the merchant
  portal (T-0085 manual step) — verify before the payment leg.
- The auto-deliver / auto-cancel Functions (T-0077/T-0083) are timer-based —
  the walk uses the manual customer-confirm path; Functions health is checked
  but not waited on.
- Seed check: `CountryConfiguration` CZ row must carry the 700 bp fee rate
  (T-0140 context) and `comgate`/`packeta` provider keys.

## Files touched (expected)

- `docs/test-plans/T-0153-e2e-walk.md` (new — evidence log)
- `infra/bicep/` + GitHub workflow env config as diagnosis dictates
- `frontend/.env*` example / deploy env docs (`NEXT_PUBLIC_API_*`)
- Small fixes wherever the walk finds them (each listed in the PR)

## Test plan reference

`docs/test-plans/T-0153-e2e-walk.md` (created by this ticket).

## Status log

- 2026-07-17 `draft → ready` — filed and groomed per direct user request
  ("task on completing the website: maker can create products for sale that
  show in the catalog and users can order those items"). Dependencies all
  `done`; the only external prerequisite is Azure access for the dev-env
  revival, listed as `manual_steps`.
- 2026-07-17 `ready → in_progress` — **dev-env revival scope is already
  satisfied**: the dopady §4 "backend down" finding is stale. Hosts moved to
  the CAF names (`app-makables-{customer,maker,admin,public}-weu-dev` +
  `web-makables-weu-dev`); all five answer 200 and
  `GET /api/v1/catalog/makers` returns valid JSON (`totalCount: 0` — empty
  catalog, as expected pre-walk). "Deploy → dev" runs green (last:
  2026-07-17 13:02). AC-1 satisfied.
- 2026-07-17 — **cookie-domain strategy decided + implemented: same-origin
  proxy** (`feat/T-0153-same-origin-api-proxy`). Browser-facing API bases
  become relative `/api-proxy/<host>` paths; `next.config.ts` rewrites them
  to the real hosts (from new `API_<HOST>_INTERNAL_BASE_URL` vars, also used
  by SSR fetches in `api-fetch.ts`). Set-Cookie thus lands first-party on the
  frontend origin — no DNS/custom-domain dependency. Chosen over the custom
  parent domain because it is code-only; the parent domain remains the better
  production endgame (per-IP rate limiting funnels through one egress IP
  under the proxy — T-0136 caveat noted in both deploy workflows).
  **New manual step:** add
  `https://web-makables-weu-dev.azurewebsites.net/api-proxy/customer/api/v1/auth/google/callback`
  (and the prod equivalent) to the Google OAuth client's authorized redirect
  URIs before OAuth login can work on deployed envs.
- Blocked-on-user residue: `az login` (refresh token expired 90d) for any
  portal-side diagnosis; not needed for the current slice.
