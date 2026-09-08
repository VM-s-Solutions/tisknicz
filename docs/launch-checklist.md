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

## Deploy readiness (T-0138 — the 6 blockers are FIXED IN CODE; these are the remaining operator steps)

T-0138 closed the 6 deploy-blockers in the Bicep/CI (makables DB + SSL, boot
app-settings + CORS fix, payouts container, EF-migration job, Functions deploy
job, secret wiring) and added a CI `bicep build` lint. A staging deploy now
yields a *working* app — once the operator does these. Full procedure:
`docs/deployment/deploy-runbook.md`.

- [ ] **Set the GitHub Actions deploy secrets (BLOCKING):** per environment
  (`dev` / `production` GitHub environments) set `AZURE_CLIENT_ID`,
  `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `POSTGRES_ADMIN_USER`,
  `POSTGRES_ADMIN_PASSWORD`, `JWT_SIGNING_KEY_BASE64`, `SENDGRID_API_KEY`,
  `COMGATE_MERCHANT_ID`, `COMGATE_SECRET`, `PACKETA_API_KEY`,
  `PACKETA_PUBLIC_WIDGET_KEY`, `MAPBOX_ACCESS_TOKEN`. A missing
  secret aborts the deploy (fail-closed). No secret value is in the repo.
  (No `VERCEL_TOKEN` — the frontend deploys to Azure App Service.)
- [ ] **Register Google + Apple sign-in (BLOCKING for prod):** the login /
  register pages ship both OAuth buttons; register the providers per
  [docs/deployment/oauth-providers.md](deployment/oauth-providers.md) and set
  `GOOGLE_OAUTH_CLIENT_ID`, `GOOGLE_OAUTH_CLIENT_SECRET`, `APPLE_SERVICES_ID`,
  `APPLE_TEAM_ID`, `APPLE_KEY_ID`, `APPLE_PRIVATE_KEY_PEM`. Dev deploys boot
  on stubs (buttons fail closed at the provider); the production deploy
  fails loudly if any of the six is missing. Apple needs the paid Developer
  Program; Google's consent screen must be switched from Testing to
  In production before launch.
- [ ] **Azure RG + OIDC federated credential (BLOCKING):** create the
  `rg-makables-weu-dev` / `rg-makables-weu-prod` resource group and the Entra app + federated
  credential bound to the GitHub environment (the workflows use OIDC, no stored
  password). See deploy-runbook §"One-time operator setup".
- [ ] **Frontend custom domain + TLS (prod, BLOCKING before go-live — not before the deploy):**
  bind `makables.cz` and `admin.makables.cz` to `web-makables-weu-prod` and issue the free
  managed certificates. Full procedure: `docs/runbooks/custom-domain-and-tls.md`.
  - **Only the frontend App Service needs a domain.** The admin console is the `(admin)` route
    group in the same Next.js app, and the four API hosts are reached through the same-origin
    `/api-proxy` rewrite, so they need no custom hostname.
  - **Sequence:** first prod deploy → verify the private DB path → bind the domain → *then* the
    go-live data chain. The DNS records cannot be created earlier: the apex `A` record needs the
    app's inbound IP and the `asuid` TXT its verification ID, both of which only exist once the
    App Service is provisioned. The startup validator only checks the URL is well-formed https, so an
    unbound domain does not block the deploy — it blocks anything that emails a user, because
    delivered mail cannot be recalled.
  - **Replace, do not add, the apex `A` record.** `makables.cz` already resolves to two
    IPs, one of which is the shared VIP behind the *dev* app. Adding a third leaves DNS
    round-robining into dead targets and can fail certificate issuance.
  - **Re-register the OAuth redirect URIs in the same session** (Google + Apple). They are
    built from `window.location.origin` at click time, so the cutover changes them and
    every social sign-in fails with `redirect_uri_mismatch` until updated. See
    `docs/deployment/oauth-providers.md`.
  - Two constraints that break certificate **renewal** silently, months later: the `admin`
    CNAME must point directly at `web-makables-weu-prod.azurewebsites.net` (no intermediate
    CNAME), and the app must carry no IP restrictions (apex renewal requires public
    reachability). Neither is set today; keep it that way.
- [x] **Prod migration connectivity (RESOLVED — by design, not by exception):**
  the prod `migrate` job runs on a GitHub-hosted runner and opens a temporary
  runner-IP firewall rule for the migration window, then deletes it. That is
  the intended design: `publicNetworkAccess` stays `Enabled` on the server so
  the rule can exist at all, while a private endpoint carries the apps' traffic
  — the two coexist by design. No self-hosted runner is needed. The delete step
  now fails the job loudly rather than swallowing the error, because with zero
  standing rules it is the only thing keeping the server closed.

## Infra hardening — Bicep ↔ ADR 0023 §7 cut-overs (T-0134)

The ops runbooks (`docs/runbooks/`) document the cut-over PROCEDURE for each gap below; the actual
infra change is the operator's pre-launch task tracked here. Each line names the shipped state, the
ADR 0023 §7 target, and the runbook that covers it.

- [ ] **Secrets to Key Vault references (hardening; not deploy-blocking after T-0138):** the Postgres
  conn string + Comgate / Packeta / SendGrid / Mapbox / JWT secrets ship as `@secure()` **App
  Settings** injected from GitHub Actions secrets (T-0138 — the app boots and no value is in the repo).
  ADR 0023 §7 wants these relocated to `@Microsoft.KeyVault(SecretUri=...)` references so they're not
  visible as plain settings in the resource group. Closes the `TODO(T-0134)` in
  `infra/bicep/main.bicep` (the KV-identity ordering cycle). Procedure: `docs/runbooks/secret-rotation.md` §C.
  **When this lands:** set Bicep param `grantKeyVaultReaderRoles = true` (it defaults `false` so the
  hosts get the "Key Vault Secrets User" role) **and** ensure the deploy identity has
  `roleAssignments/write` (User Access Administrator / Owner on the RG) — the default Contributor cannot
  create role assignments. Until then the KV is empty and the hosts read secrets as direct app settings.
- [x] **`AzureWebJobsStorage` identity-based — ALREADY SHIPPED (this item was stale).**
  `infra/bicep/modules/functions.bicep` sets `AzureWebJobsStorage__accountName` +
  `AzureWebJobsStorage__credential = managedidentity`; there is no account key and no `TODO(T-0134)`
  in that file. `role-assignments.bicep` grants the Functions MI Storage Blob Data **Owner** on its own
  host storage account (required for identity-based host storage) plus Blob Data Contributor and Queue
  Data Contributor. The app is on a Dedicated plan with `alwaysOn`, so there is no Azure Files
  dependency and therefore no `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING` key to remove.
  - **Both residuals are now closed in-pipeline**, by two steps, because one is not enough:
    - `GET /api/health` (`HealthFunction`, anonymous, dependency-free) proves the site is up, the worker
      is running and every `ValidateOnStart` options check passed. It also absorbs the RBAC-propagation
      window — identity-based host storage 403s until role assignments propagate (~10 min), so the probe
      waits that long. What was an invisible race the deploy won by luck is now a reported condition.
    - It is **not sufficient on its own**, and the checklist should not pretend otherwise: container
      validation only runs when `IsDevelopment()` and the deployed host runs as Production, so a broken
      DI graph surfaces at first invocation, not startup — and a trigger whose `%Setting%` binding does
      not resolve is reported "in error" while the host keeps serving `/api/health`. That is precisely
      the outbox-never-drains failure. So a second step reads `/admin/host/status` and asserts
      `state == Running` **with an empty `errors[]`**. Verified against stubbed host responses:
      a function-in-error payload fails the gate.
    - `HealthFunction` is anonymous by necessity (a keyed probe would need a host key, which lives in the
      very storage the probe tests). That is a **named exception to ADR 0020**, amended in the same PR,
      with HTTP concurrency caps in `host.json` as the compensating control.
- [x] **Postgres Private Endpoint (prod) — SHIPPED in `infra/bicep/modules/network.bicep`.**
  Production gets a VNet, a private endpoint on the server, the
  `privatelink.postgres.database.azure.com` zone and a vnet link; the four API hosts and Functions
  integrate into a delegated subnet. Production still runs WITHOUT the "allow all Azure services"
  rule — with zero standing firewall rules the server has no public path in. Two things remain
  **operator checks after the first prod deploy**, because ARM reports success either way:
  - `nslookup pg-makables-weu-prod.postgres.database.azure.com` from a host's Kudu console must
    return a `10.20.2.x` address, not a public one.
  - `az network private-endpoint-connection list` must show the connection **Approved**, not Pending.
  A restored server needs the endpoint re-attached — see `docs/runbooks/backup-restore.md` §1.
  Note the deploy principal needs `Microsoft.Network/virtualNetworks/subnets/join/action` and
  `.../privateEndpointConnectionsApproval/action`; Owner covers both.
- [x] **Blob redundancy (prod) — SHIPPED as `Standard_GZRS`.** `blob.bicep` takes a `skuName` param;
  `weu.prod.bicepparam` sets `Standard_GZRS`, dev keeps the `Standard_LRS` default. ADR 0023 §7 was
  **amended** (2026-09-06) from GRS to GZRS rather than silently deviated from: under GRS the
  primary-region copy is LRS, so losing one West Europe datacenter takes the account offline and the
  only recovery is a lossy customer-initiated unplanned failover. The zone axis is not a live SKU
  update, so the first-ever deploy was the only free moment to choose.
  - **Entitlement pre-flight is automated** — no operator step. `deploy-production.yml` runs a
    "Pre-flight — the blob SKU is entitled in this subscription/region" step before the Bicep apply: it
    reads the SKU and location straight out of `weu.prod.bicepparam` and queries the subscription's
    `Microsoft.Storage/skus` restrictions, failing in seconds with a named reason instead of ~20 minutes
    deep inside the `blob` module — which on a first-ever deploy would leave the plan, App Insights, the
    VNet and a fresh Postgres server behind, with the Key Vault already holding a 90-day name lock.
    SKU entitlement is subscription-scoped and this subscription is already offer-restricted for
    Postgres Flexible Server in that exact region, so it is checked rather than assumed.
- [x] **Blob + container soft-delete 30-day — SHIPPED.** `blob.bicep`'s existing `blobServices/default`
  now carries `deleteRetentionPolicy` **and** `containerDeleteRetentionPolicy`, both 30 days, in every
  environment. Container soft delete is the half that covers the catastrophic case: blob soft delete
  alone does not recover a dropped container ("You can't recover blobs in the deleted container").
  - **Versioning is deliberately NOT enabled**, and this item must not be "completed" by adding it.
    With versioning on, deleting a blob produces no soft-deleted object, so `az storage blob undelete`
    silently restores nothing — it would close this blocking item with a setting that breaks the exact
    recovery command the item exists to deliver. See the ADR 0023 §7 amendment.
- [x] **Key Vault purge-protection — ALREADY SHIPPED, prod-only (this item was stale).**
  `key-vault.bicep` ships `enablePurgeProtection: endsWith(keyVaultName, '-prod') ? true : null`
  alongside 90-day soft-delete. The name is not operator-supplied (`main.bicep` composes it from an
  `@allowed(['dev','prod'])` slug), so prod is gated structurally on every deploy path while dev stays
  purgeable and re-creatable. Deliberately left as a name-derived gate rather than a param: a param
  would default to `false` and make silent omission the failure mode.
  - **Know the consequence before the first prod deploy:** purge protection cannot be disabled by
    anyone, including Microsoft, and the vault name stays reserved for 90 days. Deleting the resource
    group and retrying a botched first prod deploy is therefore **off the table** once the vault is
    created. Confirm `kv-makables-weu-prod` is globally free first (`az keyvault show` must return
    NotFound, and `az keyvault list-deleted` must not list it).

## Security hardening (T-0136 / secops)

- [x] **Forwarded-headers wiring (DONE).** `UseMakablesForwardedHeaders` is the
  first stage of `UseMakablesPipeline`, enabled in deployed environments by
  `ForwardedHeaders__Enabled=true` from `infra/bicep/modules/app-service.bicep`,
  with `ForwardLimit = 1` as the anti-spoofing control and
  `ForwardedHeadersTests` pinning it. This unblocks the Comgate webhook IP
  allowlist and the Mapbox anonymous IP buckets noted in
  `docs/security/function-key-rotation.md`.
  Two residual rules, both BLOCKING if broken:
    - **Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.** The app wires this
      itself; the platform switch would register a *second* forwarded-headers
      middleware, and because the first truncates the entries it consumed the
      second would read an attacker-controlled one.
    - **Any Front Door / App Gateway / WAF must raise `ForwardLimit` in the same
      change**, or the recorded address becomes the wrong hop.
- [ ] **Anonymous rate limiting is still one shared bucket for browser traffic
  (Q-0039, NOT fixed by the above):** the frontend routes API calls through its
  own `/api-proxy` rewrite, so the last forwarded hop the API host sees is the
  frontend App Service egress IP. Forwarded headers fixed the partition for
  callers that reach a host directly (the Comgate webhook, direct API clients)
  but Next's `rewrites()` cannot inject the client address. See Q-0039 in
  `docs/questions/open.md`.
- [ ] **`COMGATE_WEBHOOK_ALLOWED_IPS` must be set before go-live (BLOCKING):**
  Comgate's published source ranges, comma-separated, as a GitHub environment
  secret. The allowlist is fail-closed — while it is empty every payment
  callback is rejected with 401 and no order can reach `Paid`. Also confirm the
  notification URL in the Comgate portal targets the **public API host
  directly**, never `makables.cz/api-proxy/...`; routing it through the frontend
  adds a hop whose egress IP must NEVER be added to the allowlist (that would
  admit anyone who can reach the public site).

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
- [x] **Custom metric emission (Q-0033) — DONE, no operator step.** Was: the
  ADR 0023 §4 alert table assumed custom metrics that were registered but
  never emitted, so those alert rules would have read empty. T-0165 wired the
  emission: `makables.outbox.lag_seconds` + `.stalled` + `.dispatched`,
  `makables.payments.sessions_created`, `makables.webhooks.received`,
  `makables.orders.auto_delivered` / `.auto_cancelled`. The alert *rules* still
  have to be created in Azure Monitor against these names
  (`docs/runbooks/monitoring.md`) — that is the remaining ops task, but it is
  now a task with signal behind it rather than a decision.
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
