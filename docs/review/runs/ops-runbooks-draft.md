# T-0134 — Preliminary review (ops runbooks) — DRAFT

> Reviewer preliminary notes, written in PARALLEL with the runbooks author. No runbook
> files exist yet (`docs/runbooks/` absent at read time) — this is the grounding map the
> author must hit, NOT a review of written prose. Final verdict happens at PR-open against
> the three actual files.
>
> Headline lens: **GROUNDEDNESS.** A runbook that is wrong is worse than none. Every secret
> name, Bicep path, resource name, threshold, and procedure step below is cross-checked
> against the SHIPPED code/infra. Anything an author might "plausibly invent" that is NOT
> in the code is flagged as a BLOCKER-if-stated.

## Grounding cross-check — verified-real facts the author MAY rely on

| Claim | Status | Source |
|---|---|---|
| `Jwt:SigningKeyBase64`, `Jwt:Issuer`, `Jwt:KeyId` (default `"k1"`) | REAL | `Infra.Common/Auth/JwtOptions.cs` |
| `Comgate:Secret`, `Comgate:MerchantId`, `Comgate:WebhookAllowedIps` | REAL | `Infra.Clients/Comgate/ComgateOptions.cs` |
| `Packeta:ApiKey` + `Packeta:PublicWidgetKey` (both required, KV refs in prod) | REAL | `Infra.Clients/Packeta/PacketaOptions.cs` |
| `SendGrid:ApiKey` + `SendGrid:DefaultFromAddress` | REAL | `Infra.Clients/SendGrid/SendGridOptions.cs` |
| `Mapbox:AccessToken` (single server-side token, never client) | REAL | `Infra.Clients/Mapbox/MapboxOptions.cs` |
| `ConnectionStrings__Postgres` injected to all 4 Web hosts + Functions | REAL | `app-service.bicep`, `functions.bicep`, `main.bicep` |
| `AzureWebJobsStorage` = account-key conn string on the Functions storage acct | REAL | `functions.bicep:41,60` |
| Key Vault: RBAC, soft-delete **90 days**, `Key Vault Secrets User` role on the 5 MIs | REAL | `key-vault.bicep` |
| Postgres backup retention **14 days**, `geoRedundantBackup: Disabled` | REAL | `postgres.bicep:51-54` |
| Blob storage **`Standard_LRS`**, no soft-delete policy resource | REAL | `blob.bicep:12-14` |
| Staging "allow all Azure services" firewall rule, prod runs without it | REAL | `postgres.bicep:74`, `main.bicep:91` |
| `ProcessOutbox` HTTP escape-hatch: `POST /api/outbox/process`, `AuthorizationLevel.Function` | REAL | `Functions/Outbox/ProcessOutboxFunction.cs:58-65` |
| Admin retry/ack UI: `POST /api/v{ver}/outbox-events/{id}/retry` + `/acknowledge` | REAL | `Web.Admin/Controllers/OutboxEventsController.cs` |
| Stalled-outbox KPI: `GET /api/v{ver}/outbox-events/stalled/count` | REAL | same controller |
| Stalled predicate `ProcessedAt==null && NextRetryAt==null && LastErrorKind!=None` | REAL | `StalledOutboxPredicateTests.cs`, controller XML doc |
| `admin_audit_log` append-only via DB triggers `trg_admin_audit_log_reject_update/_delete` | REAL | migration `20260523110529_OutboxAndAuditLog.cs:88-109` |
| Timer schedules + `ValidateOnStart` boot-fail behavior | REAL | `env-vars.md`, `AddMakablesAuth.cs:69-73` |
| ADR §4 alert thresholds (5xx>1%/5m Sev2; webhook 5xx>5%/5m Sev1; outbox lag>5m Sev2; stalled>10 Sev3; DB CPU>80%/10m Sev2; failed-login>50/min/IP Sev3; auto-deliver any-fail Sev2) | REAL | ADR 0023 §4 |

## BLOCKERS — must be correct or the runbook is dangerous

### B-1 — JWT rotation: the dual-key grace window is NOT supported by the code. Author must take the "all sessions drop" branch.
AC-2 offers EITHER a dual-key/`kid` grace window OR the "all sessions drop" tradeoff.
**The code only supports the latter.** `AddMakablesAuth.cs:99-112` wires a SINGLE
`IssuerSigningKey = signingKey` — there is no `IssuerSigningKeys` collection, no secondary
key, no `kid`-based resolver. `JwtOptions.KeyId` exists but its own XML doc says
*"Single active key at launch; rotation lives in the next ADR."* `JwtIssuer.cs:43` stamps one
`kid` onto issued tokens; nothing validates against more than one key.
→ If the runbook claims a graceful/zero-downtime dual-key JWT rotation, that is an INVENTED
capability = **BLOCKER**. The runbook MUST state the real blast radius: rotating
`Jwt:SigningKeyBase64` invalidates **every live access token** platform-wide (all 4 audiences)
the moment the new key is picked up; clients get 401 and must re-auth.
**Refresh-token nuance (verify, don't paper over):** access tokens are HS256-signed (die on
rotation); **refresh tokens are random opaque values stored as SHA-256 hashes in
`refresh_tokens`** (`RefreshTokenConfiguration.cs`) — they are NOT JWT-signed, so they
SURVIVE a signing-key rotation. So the honest impact is: "active 15-min access tokens are
killed; the next `/auth/refresh` call mints a fresh access token under the new key — users see
at most one failed request, then a transparent refresh, UNLESS their refresh token is also
expired/revoked." The author must get this distinction right; claiming refresh tokens also
drop (or claiming nothing drops) is wrong in opposite directions.

### B-2 — Monitoring: most ADR §4 custom metrics are NOT emitted yet. The outbox-stall signal is a DB-query endpoint, not a metric.
`AddMakablesObservability.cs:24-27` and `MakablesMeters.cs:7-12` are explicit: only meter
*names* are registered; the instruments "are added … in their owning modules **in later
tickets**." The ONLY concrete custom instruments shipped are payouts
(`makables.payouts.*`, `PayoutMetrics.cs`). So `outbox_lag_seconds`,
`outbox_stalled_count`, `payment_create_failures_total`, `webhook_received_total`,
`auto_deliver_count` are **not yet flowing to Azure Monitor.**
→ A monitoring runbook that tells the operator to build an Azure Monitor metric alert on
`outbox_stalled_count` today would point at a signal that does not exist = **BLOCKER** unless
flagged. Required honesty: the **real, shipped** outbox-stall signal is the admin KPI endpoint
`GET /outbox-events/stalled/count` (DB-backed) + the triage list — the first-response is the
admin retry/ack UI (real) and the `POST /api/outbox/process` Function escape-hatch (real).
The §4 metric-based alerts should be framed as "alert wired once the instrument lands (tracked
separately)" — the runbook is grounded if it routes first-response through what exists and
names the metric gap, aspirational/dangerous if it pretends the metrics alert today.
This is exactly the pre-flight test case — and it currently FAILS the "real signal" bar unless
the author writes it against the endpoint, not the metric.

### B-3 — `CRON_SECRET` is a documented convention, NOT wired on the shipped Functions.
The ticket's secret row lists `CRON_SECRET` / "Functions host+function keys." Reality: the two
HTTP-triggered Functions (`ProcessOutbox`, `RunWeeklyPayoutBatch`) both use
`AuthorizationLevel.Function` (Azure Functions **host/function keys**) — there is NO code
reading a `CRON_SECRET` bearer header (grep: it lives only in ADR 0005, CLAUDE.md, security
checklist, and webhook-verification.md as a *convention*).
→ The rotation procedure for this row MUST be "regenerate the Functions host/function key in
the portal / `az functionapp keys`," NOT "rotate the `CRON_SECRET` Key Vault secret." Telling
an operator to rotate a `CRON_SECRET` that nothing consumes on these endpoints = invented
procedure = **BLOCKER**. Honest framing: `CRON_SECRET` is the ADR-0005 convention for a
future `/api/public/cron/*` surface; the shipped Function triggers gate on the host key.

### B-4 — `AzureWebJobsStorage` is the Functions/queue account, distinct from the app-data Blob account. Don't conflate them.
The env-vars doc loosely calls `AzureWebJobsStorage` the "Blob/queue conn string," but there
are TWO storage accounts: `makables{env}fn` (Functions runtime + outbox handoff queues,
`AzureWebJobsStorage`, `functions.bicep`) and `makables{env}blob` (product-images / invoices /
maker-documents / order-attachments containers, `blob.bicep`, read via `AzureBlobStorage:`
`ConnectionString`/`ServiceUri`). Rotating the storage **account key** is per-account.
→ A "rotate the Blob conn string" step that conflates the two (e.g. claims rotating
`AzureWebJobsStorage` also re-keys invoice access) = **BLOCKER**. The secret-rotation runbook
should have the `AzureWebJobsStorage` row (Functions acct key) AND, ideally, an
`AzureBlobStorage` row (app-data acct key / the MI cut-over) as distinct entries.

## MAJOR — grounding / honesty, fix before approve

- **M-1 (AC-5 gap honesty).** All three §7-divergence gaps must be named with ADR target +
  launch-checklist line: (a) blob `Standard_LRS` vs §7 **GRS + 30-day soft-delete**; (b)
  plaintext `ConnectionStrings__Postgres` App Setting → KV reference (`TODO(T-0134)` at
  `main.bicep:95`); (c) `AzureWebJobsStorage` account-key → identity-based
  (`TODO(T-0134)` at `functions.bicep:37`). NOTE the backup retention is **14 days**, and
  §7 names a **7-day** PITR window — 14≥7 satisfies the floor, so do NOT write "retention
  gap"; the real gap there is **`geoRedundantBackup: Disabled`** vs §7 prod-GRS intent. A
  runbook claiming "retention must be raised to 7" would be backwards.
- **M-2 (launch-checklist).** `docs/launch-checklist.md` exists but currently has only Legal +
  SEO sections — the three Bicep cut-over lines the runbooks reference are NOT there yet. The
  ticket's Modified list says the implementer adds them. If the runbooks cite a
  launch-checklist line that does not exist, AC-5's "points at the launch-checklist line" is
  unmet. Verify the lines are added in the SAME PR.
- **M-3 (IaC path drift).** ADR 0023 §7 says IaC lives in `deploy/bicep/`; the shipped infra is
  in `infra/bicep/`. Runbooks must cite the REAL path `infra/bicep/modules/*.bicep`. Any
  `deploy/bicep/...` reference copied from the ADR prose is an invented path = fix.
- **M-4 (PITR restore vs append-only audit + immutable invoices — get the mechanics right).**
  Postgres PITR (`az postgres flexible-server restore`) creates a **NEW server** from a
  point-in-time; it does NOT UPDATE/DELETE rows on the live table, so the `admin_audit_log`
  append-only triggers are NOT an obstacle to restore (they re-create with the schema). The
  honest hazard to flag is the OTHER direction: a PITR rewind moves the whole DB back in time,
  so audit entries AND issued invoice numbers after the restore point are **lost from the new
  server** — re-issuing into an already-used invoice-number range is the numbering-sequence
  hazard (AC-4). A runbook claiming the trigger "blocks restore" or that you "restore the audit
  table separately" would be wrong.

## MINOR / NITS

- **N-1.** Blob recovery: §7 wants 30-day soft-delete + GRS-failover, but neither is configured
  in `blob.bicep` today — so the "soft-delete restore" procedure is documenting a capability
  that must be ENABLED first. Frame as "after the launch-checklist GRS/soft-delete cut-over,"
  not as available now.
- **N-2.** Key Vault soft-delete is **90 days** (`key-vault.bicep:23`), not 30 — handy for an
  "I deleted the wrong secret version, recover it" note in the rotation runbook; cite 90.
- **N-3.** Use the real route casing: the admin endpoint group is `outbox-events`
  (hyphen), and the Function HTTP route is `outbox/process`. Ticket prose says "T-0118c";
  the shipped controller traces to T-0109/T-0126/T-0127 — cite the route, not the ticket id.
- **N-4 (AC-6 manual flag).** Each runbook's closing "Verification (manual, staging dry-run)"
  must explicitly say the dry-run is NOT YET executed (claims-tested = fail). Frontmatter
  `manual_steps` already records it; the prose must match, not over-claim.
- **N-5 (AC-7 size/hygiene).** Doc-only; `scripts/check-consistency.mjs` T1–T9 don't engage
  (no `.cs/.ts/.tsx/.mjs` touched). If the OPTIONAL Bicep `TODO` cross-ref edits are made,
  they must stay comment-only (no behavioral change) or they pull T-rules into scope.

## Preliminary verdict

**NOT APPROVABLE as-scoped until the four BLOCKERS are written correctly.** None are about
prose quality — all four are groundedness traps where the ticket/ADR prose invites a
plausible-but-false claim (graceful JWT rotation, metric-based outbox alert, `CRON_SECRET`
rotation, single "blob" account). The infra facts are otherwise well-grounded and the author
has a clear real-signal path for every row. Re-review at PR-open against the three files;
gate on B-1…B-4 + M-1…M-4 specifically.
