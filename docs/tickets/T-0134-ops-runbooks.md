---
id: T-0134
title: Production ops runbooks — secret rotation, monitoring, backup/restore
status: ready
size: M
owner: secops
created: 2026-06-21
updated: 2026-06-21
depends_on: [T-0016, T-0014]
blocks: []
user_stories: []
adrs: [0023]
phase: 6
manual_steps:
  - actor: SecOps
    timing: pre-launch
    description: >-
      Per-runbook "Verification (manual, staging dry-run)" section must be executed on a
      scratch/staging environment before launch. Each procedure is written now; the dry-run
      that proves it works is the manual gate. (1) secret-rotation: rotate ONE non-critical
      secret end-to-end on staging and confirm pickup; (2) monitoring: fire one synthetic
      alert (e.g. force an outbox stall) and walk the first-response; (3) backup-restore:
      run a real Postgres PITR restore + a Blob soft-delete recovery on a scratch env.
    rollback: >-
      Staging-only dry-runs; no production impact. A failed dry-run blocks launch and
      re-opens the runbook for correction — it does not roll anything back in prod.
security_touching: yes
layers: [docs]
---

# T-0134 — Production ops runbooks (secret rotation · monitoring · backup/restore)

## Context

Pre-launch ops readiness. Three operating runbooks in a NEW `docs/runbooks/` directory, grounded in the **shipped** T-0016 Bicep infra (App Services × 4 + Functions on a shared plan, Postgres Flexible Server 16, Blob storage, Key Vault with 90-day soft-delete + RBAC, App Insights/Log Analytics) and the T-0014 OpenTelemetry/Serilog observability stack, against the operational posture in **ADR 0023 §7 (deployment topology — secrets, backup/recovery)** and **§4 (observability — the alert table)**.

These are **real operating procedures, not aspirational**. Every step references actual infra: Bicep module paths (`infra/bicep/modules/*.bicep`), the real secret/option names from `docs/deployment/env-vars.md` and the `AddMakables*`/`*Options` classes (`Jwt:SigningKeyBase64`, `Comgate:Secret`, `Packeta:ApiKey`, `SendGrid:ApiKey`, `Mapbox:AccessToken`, `ConnectionStrings__Postgres`, `AzureWebJobsStorage`, Functions keys), and the real alert signals ADR 0023 §4 defines (5xx rate, webhook 5xx, `outbox_lag_seconds`, `outbox_stalled_count`, DB CPU). The `ValidateOnStart` options classes are the safety net the runbooks lean on: a bad rotation → the host refuses to boot, so the operator catches a broken secret at deploy, not at first customer request.

Doc-only. No code, no schema, no NSwag, no error codes, no i18n. Owner **secops** — this is the platform's security/ops posture. The runbooks ALSO close three `TODO(T-0134)` markers already planted in the Bicep + env-vars doc (plaintext Postgres conn string → Key Vault reference; `AzureWebJobsStorage` → identity-based; the prod Postgres Private Endpoint that replaces the staging "allow all Azure services" firewall rule). The runbooks document the cut-over **procedure**; the actual cut-over is the operator's pre-launch task tracked in `docs/launch-checklist.md`.

**Honesty rule (grounded, not aspirational):** ADR 0023 §7 is the target posture, but the shipped Bicep diverges in three places the runbooks must call out as pre-launch gaps, NOT silently paper over — (a) `blob.bicep` is `Standard_LRS` with no soft-delete policy configured; ADR §7 wants **GRS + 30-day soft-delete in production**; (b) `postgres.bicep` sets `backupRetentionDays: 14` + `geoRedundantBackup: Disabled`; ADR §7 names a **7-day PITR window** (14 ≥ 7 satisfies the floor, but GRS-for-prod is unmet); (c) secrets are currently plain App Settings, not Key Vault references. Each runbook names the gap, the ADR target, and points at the launch-checklist line that closes it.

## Deliverables (checklist)

- [ ] **`docs/runbooks/secret-rotation.md`** (~120–200 lines) — rotation procedure for every secret in the platform.
  - **§A scope (one row per secret):** `Jwt:SigningKeyBase64`, `Comgate:Secret`, `Packeta:ApiKey` (+ `Packeta:PublicWidgetKey`), `SendGrid:ApiKey`, `Mapbox:AccessToken`, `ConnectionStrings__Postgres` (Postgres conn string), `AzureWebJobsStorage` (Blob/queue conn string), `CRON_SECRET` / Azure Functions host+function keys.
  - For EACH secret: **where it lives** (Key Vault secret name + the `@Microsoft.KeyVault(SecretUri=...)` App Setting / `*Options` binding that reads it), **how to rotate at the provider** (regenerate at Comgate portal / Packeta client zone / SendGrid dashboard / Mapbox account / rotate the Postgres admin password / regenerate the storage account key / cycle the Functions key), **how to update the Key Vault secret** (`az keyvault secret set`, new version), **how App Services pick it up** (Key Vault reference auto-refresh cadence vs. an explicit App Service restart / slot recycle), and **blast radius / downtime**.
  - **JWT signing key** gets its own subsection: rotation must not invalidate live refresh tokens mid-flight. Capture EITHER the dual-key / grace-window approach (`JwtOptions.KeyId` `"k1"` → `"k2"`; validate against both keys during the window; the `kid` claim is already wired for exactly this) OR the accepted "all active sessions drop on rotation" tradeoff, with the call made explicitly and the customer-impact (forced re-login) stated.
  - Reference `ValidateOnStart` as the safety net: a bad/absent secret on a `ValidateOnStart` options class → host refuses to boot at deploy, surfacing the broken rotation before it reaches a customer.

- [ ] **`docs/runbooks/monitoring.md`** (~120–200 lines) — alert thresholds + signal meaning + first-response.
  - **§A scope (one row per signal, threshold from ADR 0023 §4):** Customer API **5xx rate** (>1% / 5 min, Sev 2); Webhook handler **5xx rate** (>5% / 5 min, Sev 1); **outbox lag** `outbox_lag_seconds` (>5 min, Sev 2); **outbox stalled count** `outbox_stalled_count` (>10, Sev 3) — the same stalled set the admin dashboard surfaces (T-0109/T-0126, predicate `ProcessedAt==null && NextRetryAt==null && LastErrorKind!=None`); **Database CPU** (>80% / 10 min, Sev 2); failed-login rate (>50/min/IP, Sev 3); auto-deliver crashed (any failure, Sev 2). Plus the App Insights traces/metrics surface from T-0014 (correlation_id, the custom metrics list).
  - For EACH: **what the signal means**, the **dashboard / KQL query** to check in App Insights / Log Analytics, the **likely cause**, and the **first-response action** — e.g. stalled outbox → admin retry/ack UI (T-0118c) or the `ProcessOutbox` HTTP escape-hatch (T-0029); webhook 5xx → check Comgate/Packeta origin + the IP allowlist; DB CPU → check the slowest query + the burstable-SKU credit balance.

- [ ] **`docs/runbooks/backup-restore.md`** (~120–200 lines) — recovery procedures.
  - **§A scope:** (1) **Postgres PITR** restore procedure (the §7 7-day window; `az postgres flexible-server restore` to a new server at a point-in-time, then re-point the conn-string secret + restart; staging is 1-day per §7); (2) **Blob recovery** — soft-delete restore (§7 target 30-day) + GRS failover considerations (with the LRS-vs-GRS prod gap called out); (3) **admin-data considerations** — `AdminAuditLogEntry` is append-only / trigger-protected (note what a restore implies for the audit chain) and invoices are immutable legal records (note the restore-vs-immutability interaction; a PITR rewind that "un-issues" an invoice number is a numbering-sequence hazard to flag).
  - Restore VERIFICATION (an actual restore dry-run on a scratch env) is the **manual_step** — the PROCEDURE is written now; the §7 "manual restore test once per quarter" cadence is referenced.

Each runbook ends with a **"Verification (manual, staging dry-run)"** section flagged as the ticket's `manual_step` (see frontmatter).

## Out of scope

- Any code, Bicep, NSwag, migration, error code, or i18n change. Doc-only.
- Actually executing the rotations / restores in production (the dry-run is a pre-launch manual_step; the real Key Vault-reference + Private-Endpoint + GRS cut-overs are launch-checklist items, NOT this ticket).
- The k6 load test (T-0132), accessibility audit (T-0133), and bug-bash smoke (T-0135) — separate Phase 6 tickets.
- Closing the Bicep ADR-divergence gaps (LRS→GRS, plain-setting→Key-Vault-ref, Private Endpoint) — the runbooks DOCUMENT the cut-over procedure and flag the gaps; the infra change is a follow-up tracked on `docs/launch-checklist.md`.

## Acceptance criteria

- **AC-1 (secret-rotation.md)** Given the runbook, when an operator reads any one of the 8 secret rows, then they find: the Key Vault secret name + the App Setting / `*Options` binding that reads it (citing the real names — `Jwt:SigningKeyBase64`, `Comgate:Secret`, `Packeta:ApiKey`, `SendGrid:ApiKey`, `Mapbox:AccessToken`, `ConnectionStrings__Postgres`, `AzureWebJobsStorage`, Functions key), the provider-side rotation step, the `az keyvault secret set` update step, the App Service pickup behavior (auto-refresh vs. restart), and the blast radius / downtime. Each cited name resolves to a real entry in `docs/deployment/env-vars.md` or a `*Options.cs` class.
- **AC-2 (secret-rotation.md — JWT)** Given the JWT signing-key subsection, when read, then it states explicitly whether rotation uses the dual-key grace-window (`JwtOptions.KeyId` `k1`→`k2`, the `kid` claim already wired) OR accepts the "all sessions drop" tradeoff, AND names the customer impact (forced re-login) and how live refresh tokens are handled. It references `ValidateOnStart` as the boot-time safety net for a botched rotation.
- **AC-3 (monitoring.md)** Given the runbook, when an operator reads any one signal row, then they find: the ADR 0023 §4 threshold + severity, the App Insights/Log Analytics dashboard or KQL query to check, the likely cause, and the first-response action. The outbox-stall row points at the T-0118c admin retry/ack UI and the T-0029 `ProcessOutbox` HTTP escape-hatch; the thresholds match the ADR §4 alert table exactly (5xx >1%/5m, webhook 5xx >5%/5m, outbox lag >5m, stalled >10, DB CPU >80%/10m).
- **AC-4 (backup-restore.md)** Given the runbook, when read, then it documents: the Postgres PITR restore procedure (§7 7-day prod window, `az postgres flexible-server restore` + conn-string re-point + restart), the Blob soft-delete + GRS recovery (with the LRS-vs-§7-GRS prod gap flagged), and the admin-data considerations (`AdminAuditLogEntry` append-only/trigger-protected restore implication + invoice-immutability / numbering-sequence-rewind hazard). The actual restore dry-run is named as the manual_step.
- **AC-5 (grounding honesty)** Given all three runbooks, when reviewed against the shipped Bicep, then every cited infra detail resolves to a real `infra/bicep/modules/*.bicep` resource or a documented option, AND the three ADR-divergence gaps (blob `Standard_LRS` vs §7 GRS; the `TODO(T-0134)` plaintext Postgres conn string → Key Vault reference; `AzureWebJobsStorage` → identity-based) are each named with the ADR §7 target and the launch-checklist line that closes them — not silently omitted.
- **AC-6 (manual step)** Each runbook carries a final "Verification (manual, staging dry-run)" section, and the ticket frontmatter `manual_steps` records the SecOps pre-launch dry-run with timing + rollback. The procedure is written; the dry-run is the gate.
- **AC-7 (size + consistency + hygiene)** Each runbook is ~120–200 lines, real and actionable. `node scripts/check-consistency.mjs` exits 0 (doc-only change touches no `.cs`/`.ts`/`.tsx`/`.mjs` files, so T1–T9 do not engage). T8/T9 are N/A (docs — no `BusinessErrorMessage` codes, no unique indexes introduced). No code, no NSwag regen, no migration.
- **AC-8 (INDEX)** `docs/tickets/INDEX.md` shows T-0134 in `ready` state in the Phase 6 block.

## Files

### New
- `docs/runbooks/secret-rotation.md`
- `docs/runbooks/monitoring.md`
- `docs/runbooks/backup-restore.md`

### Modified
- `docs/tickets/INDEX.md` — T-0134 `draft → ready`.
- `docs/launch-checklist.md` — add the three Bicep ADR-divergence cut-over lines (LRS→GRS, plaintext-conn-string→Key-Vault-ref, `AzureWebJobsStorage`→identity-based, Postgres Private Endpoint) the runbooks reference (if the file does not yet exist, this ticket's implementer may stub the relevant lines; the legal/Q-0030 lines are owned elsewhere).
- `infra/bicep/main.bicep` / `infra/bicep/modules/postgres.bicep` / `infra/bicep/modules/functions.bicep` — OPTIONAL: update the existing `TODO(T-0134)` comments to point at the now-written runbook section (doc cross-reference only; no behavioral Bicep change). Skippable if it risks scope-creep.

## Commits hint

ONE commit (doc-only bundle):

```
docs(ops-runbooks): groom T-0134 to ready — secret rotation + monitoring + backup-restore
```

The three runbook authoring commits are the implementer's (one per runbook OR one bundled), landed on `feat/ops-runbooks`, secops-owned. This grooming commit ships the ticket + the INDEX flip only.

## Definition of Ready (DoR)

1. **not-duplicate** — confirmed against INDEX.md; T-0134 is the sole runbooks ticket. No ADR conflict (extends ADR 0023 §7 operational posture; introduces no new decision).
2. **observable G/W/T AC** — AC-1…AC-8 above are Given/When/Then with verifiable artifacts (each cited secret/signal name resolves; thresholds match ADR §4; gaps named; consistency exit 0).
3. **sized S/M/L** — **M** (three ~120–200-line runbooks grounded in real infra; doc-only, no code). Not Large; no split needed.
4. **depends_on done or unblocker** — `T-0016` (infra) **done**, `T-0014` (observability) **done**. No chain-waiting.
5. **manual_steps populated** — yes: per-runbook staging dry-run, SecOps actor, pre-launch timing, staging-only rollback.
6. **security_touching set** — **yes** (documents secret rotation + the security/ops posture).
7. **layers populated** — `[docs]` (doc-only; no backend/frontend/db/config/infra behavioral change).

## Status log

- 2026-06-21 `draft` by PM. Phase 6 polish ticket from INDEX row T-0134 ("Production secret rotation playbook + monitoring playbook + restore-from-backup playbook"). depends_on T-0016 (Bicep infra) + T-0014 (observability), both done.
- 2026-06-21 `draft → ready` by PM (secops-owned). Lean doc-only bundle on `feat/ops-runbooks`. Three runbooks scoped + grounded in the shipped T-0016 Bicep (`infra/bicep/modules/*.bicep`), the real secret/option names (`docs/deployment/env-vars.md` + the `*Options.cs` classes), and ADR 0023 §4 (alert table) + §7 (secrets, backup/recovery). DoR 7/7 met. `security_touching: yes`. Per-runbook staging dry-run flagged as the SecOps pre-launch `manual_step` (procedure written now; dry-run is the gate). Honesty rule recorded: the runbooks must name the three shipped-Bicep-vs-ADR-§7 gaps (blob LRS vs GRS; plaintext Postgres conn string → Key Vault ref; `AzureWebJobsStorage` → identity-based) against the launch-checklist, not paper over them. `node scripts/check-consistency.mjs` exits 0 (doc-only; T1–T9 do not engage). **Ready for secops.**
