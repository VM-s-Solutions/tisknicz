# Makables — Launch checklist

Blocking pre-launch action items. Each line is gated; go-live is blocked until
every BLOCKING item is resolved. Maintained alongside the tickets that surface
the gap (the ticket scaffolds the route/feature; the line tracks the missing
input that only the operator can supply).

## Legal

- [ ] **Legal text (Q-0030, BLOCKING):** JVM YORE s.r.o. must supply approved
  VOP (obchodní podmínky) + GDPR privacy/cookie text. Pages `/vop` + `/gdpr` are
  scaffolded (shell + nav-reachable route + i18n keys + a visible placeholder
  banner) by T-0130; only the legal TEXT is missing. Before go-live: replace the
  `static.legal_placeholder.banner` Alert and populate the `static.terms.*` /
  `static.privacy.*` keys with the approved text. See `docs/questions/open.md`
  Q-0030 (incl. the open sub-question on a cookie-consent banner / cookie
  management UI — confirm whether launch needs one).

## Infra hardening — Bicep ↔ ADR 0023 §7 cut-overs (T-0134)

The ops runbooks (`docs/runbooks/`) document the cut-over PROCEDURE for each gap below; the actual
infra change is the operator's pre-launch task tracked here. Each line names the shipped state, the
ADR 0023 §7 target, and the runbook that covers it.

- [ ] **Secrets to Key Vault references (BLOCKING):** the Postgres conn string + Comgate / Packeta /
  SendGrid / Mapbox / JWT secrets currently ship as **plain App Settings** (`app-service.bicep`,
  `functions.bicep`); ADR 0023 §7 wants `@Microsoft.KeyVault(SecretUri=...)` references. Closes the
  `TODO(T-0134)` in `infra/bicep/main.bicep`. Procedure: `docs/runbooks/secret-rotation.md` §C.
- [ ] **`AzureWebJobsStorage` identity-based (BLOCKING):** move the Functions storage connection from
  an embedded account key to `AzureWebJobsStorage__accountName` + a managed-identity role assignment.
  Closes the `TODO(T-0134)` in `infra/bicep/modules/functions.bicep`. Procedure:
  `docs/runbooks/secret-rotation.md` §7 + §C.
- [ ] **Postgres Private Endpoint (prod, BLOCKING):** production runs WITHOUT the staging
  "allow all Azure services" firewall rule (`postgres.bicep` `allowAllAzureServices`); wire a Private
  Endpoint / VNet rule so the Web + Functions hosts can reach Postgres. A restored server needs this
  re-wired too. Procedure: `docs/runbooks/backup-restore.md` §1.
- [ ] **Blob GRS (prod, BLOCKING):** `blob.bicep` ships `Standard_LRS`; ADR 0023 §7 wants
  `Standard_GRS` in production. Until then, blob data has no geo-failover. Procedure:
  `docs/runbooks/backup-restore.md` §2b + §C.
- [ ] **Blob soft-delete 30-day (BLOCKING):** `blob.bicep` configures no soft-delete / versioning
  policy; ADR 0023 §7 wants 30-day soft-delete. Until then, accidental blob deletes are NOT
  recoverable. Procedure: `docs/runbooks/backup-restore.md` §2a + §C.
- [ ] **Key Vault purge-protection (recommended):** `key-vault.bicep` enables 90-day soft-delete but
  not purge-protection — consider enabling so secrets can't be hard-purged. Procedure:
  `docs/runbooks/backup-restore.md` §3.

## Security hardening (T-0136 / secops)

- [ ] **Forwarded-headers prerequisite for rate limiting (BLOCKING IF a reverse
  proxy is introduced):** the T-0136 rate limiter partitions anonymous traffic
  by the **raw connection IP** (`AddMakablesRateLimiting.DefaultPartition` /
  `PartitionAuth`). The current deploy is direct Azure App Service (no Front
  Door / App Gateway / WAF in `infra/bicep/`), so the connection IP IS the real
  client and the limiter works correctly. **The moment any reverse proxy / WAF /
  CDN is placed in front of the hosts, `UseForwardedHeaders` (with a restricted
  `KnownProxies` / `KnownNetworks` limited to the proxy's ranges) MUST be wired
  into `UseMakablesPipeline` in the same change, plus a regression test** —
  otherwise every request collapses to the single proxy IP (one shared
  bucket = self-DoS of all legitimate anonymous users) or an un-validated
  `X-Forwarded-For` becomes a trivial bypass. The code intentionally does NOT
  trust `X-Forwarded-For` today. Same prerequisite already noted in
  `docs/security/function-key-rotation.md` for the Mapbox anonymous path —
  this extends it to the global + `auth` limiters.

## SEO (T-0131)

- [ ] **Site URL env:** set `NEXT_PUBLIC_SITE_URL=https://makables.cz` in the
  production/staging environment (the canonical-host base for
  sitemap/robots/canonical/og:url; read only via `lib/seo/site-url.ts`).
  Defaults to `https://makables.cz` at build time; localhost is the dev
  default. After deploy, verify `/sitemap.xml` + `/robots.txt` resolve and
  submit the sitemap to Google Search Console.
- [ ] **OG image asset (follow-up, non-blocking):** add a brand OG image
  (`frontend/public/og-default.png`, 1200×630) and wire it into
  `lib/seo/site-url.ts` so every page inherits a `summary_large_image` card.
  MVP ships text-only `summary` cards (no image asset exists yet).
- [ ] **Product sitemap enumeration (deferred):** `/produkt/{productId}` URLs
  are NOT in the sitemap at MVP — there is no bulk product-id read (products
  are reachable only through a maker profile). Maker profiles
  (`/katalog/{slug}`) ARE enumerated. A backend bulk-id feed would enable
  product enumeration post-MVP.
- [ ] **Custom metric emission (Q-0033, pre-launch decision):** the ADR 0023
  §4 alert table (outbox lag/stalled, payment failures, webhook received,
  auto-deliver) assumes custom metrics that are REGISTERED but not yet
  EMITTED — only `makables.payouts.*` records values today. The
  `monitoring.md` runbook leads with the working DB-backed outbox-stall
  signal (`GET /outbox-events/stalled/count` + admin UI) + the
  ProcessOutboxTimer tick log; 5xx + DB-CPU alerts use Azure-Monitor
  built-ins (which work). Decide per Q-0033: wire the emission pre-launch,
  or accept the documented alternatives for MVP.
- [ ] **k6 load test RUN (T-0132, gated manual step):** execute
  `deploy/load-tests/makables-load.js` (100 VUs, 30-min) against live seeded
  staging per `deploy/load-tests/README.md`. PASS = the ADR 0023 §1 k6
  thresholds met (catalog p95<400/p99<1000, product p95<350, order
  p95<600/p99<1500) + zero 5xx + Postgres CPU <70% (verified out-of-band in
  the Azure metrics blade). The script + thresholds ship in this repo; the
  RUN is the pre-launch step (Ops/QA).
- [ ] **Manual a11y RUN (T-0133, gated manual step):** NVDA + Firefox Czech
  screen-reader pass + keyboard-only nav + a live-page color-contrast
  spot-check (the AA leg jsdom can't evaluate) on the critical customer
  paths, per `docs/test-plans/a11y-manual-checklist.md`. The automated
  jest-axe gate runs in CI; this manual pass is the pre-launch complement
  (QA + screen reader).

## Terminal bug bash (T-0135)

- [ ] **Final smoke RUN (T-0135, gated manual step — MVP close-out):** execute
  the 40-row end-to-end smoke against seeded staging + provider sandboxes
  (Comgate / Packeta / SendGrid / ARES / Mapbox), per
  `docs/test-plans/T-0135-smoke-checklist.md`: public/auth surface, the
  customer order money-path (place → Zásilkovna → Comgate-sandbox pay →
  server-verified Paid → invoice), maker fulfilment, admin control-plane +
  audit rows, Functions/outbox, and cross-cutting (no console errors, no
  untranslated error codes, responsive, Czech date/currency). The static
  code-side sweep + the dead-CTA fix + the link-hygiene regression test ship
  in the T-0135 PR; this RUN is the human pre-launch pass (QA/Ops). A finding
  becomes a follow-up ticket, not a launch blocker per se — but the
  money-path rows (place → pay → Paid → invoice → payout) MUST pass before
  go-live.
