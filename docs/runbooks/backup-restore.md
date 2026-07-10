# Runbook — Backup & restore

> **Scope:** how to recover Makables data — Postgres point-in-time restore, Blob soft-delete / GRS
> recovery — and the admin-data considerations that make a naive restore dangerous (the
> append-only audit log; immutable invoices + their numbering sequence). Grounded in the shipped
> T-0016 Bicep (`infra/bicep/modules/postgres.bicep`, `blob.bicep`) and ADR 0023 §7
> (Backup and recovery).
>
> **Owner:** SecOps. **Cadence:** ADR 0023 §7 mandates a **manual restore test once per quarter** in
> a scratch environment; findings logged. **Audience:** an operator with `Contributor` on the
> resource group.

## 0. What is backed up, and how

| Asset | Mechanism | Retention (shipped) | ADR 0023 §7 target |
|---|---|---|---|
| Postgres | Azure-managed automatic backups (PITR) | **14 days** (`postgres.bicep` `backupRetentionDays: 14`) | 7-day PITR (prod), 1-day (staging) |
| Postgres geo | `geoRedundantBackup: Disabled` | none | (not required at MVP) |
| Blob storage | soft-delete + redundancy | ⚠ **none configured** | 30-day soft-delete + **GRS** (prod) |
| Key Vault secrets | soft-delete | 90 days (`key-vault.bicep`) | — |

**Honesty note (gaps, named — see §C):** the shipped Postgres backup window (14 days) **exceeds**
the §7 7-day floor — fine. But `blob.bicep` ships `Standard_LRS` with **no soft-delete policy and no
GRS**, which **does not meet** the §7 prod target (GRS + 30-day soft-delete). Recovery procedures
below assume the §7 target; where the shipped infra can't deliver it yet, the step is flagged.

---

## 1. Postgres point-in-time restore (PITR)

Azure Postgres Flexible Server PITR restores to a **new** server at a chosen timestamp within the
retention window. It does **not** overwrite the live server. The standard recovery is: restore to a
new server → validate → re-point the connection-string secret → restart hosts.

**When to use:** data corruption, a bad migration, an accidental bulk delete, or ransomware. For a
*single-row* mistake, prefer a targeted fix or a side-by-side restore + selective copy rather than a
full rewind (see the invoice/audit hazards in §B).

**Procedure:**
1. **Pick the target timestamp** — the last known-good moment *before* the incident. Stay within the
   retention window (14 days shipped / 7-day §7 floor; staging is 1-day).
2. **Restore to a new server:**
   ```bash
   az postgres flexible-server restore \
     --resource-group <rg> \
     --name pg-makables-weu-prod-restored \
     --source-server pg-makables-weu-prod \
     --restore-time "2026-06-21T09:30:00Z"
   ```
3. **Validate the restored server** before touching production: connect, spot-check row counts on
   `orders`, `invoices`, `outbox_event`, `admin_audit_log`. Confirm the timestamp is right.
4. **Re-point the connection-string secret** to the restored server's FQDN:
   - `az keyvault secret set --name postgres-connstring --value "Host=pg-makables-weu-prod-restored.postgres.database.azure.com;Database=makables;Username=...;Password=...;SslMode=Require"`
   - (Or update the `ConnectionStrings__Postgres` App Setting until the Key Vault-reference cut-over
     lands — see `secret-rotation.md` §C.)
5. **Restart hosts in order:** `Web.Customer` is the migration runner (ADR 0023 §7); restart it
   first, confirm it's healthy and the readiness check passes, then restart Maker / Admin / Public /
   Functions. Their startup readiness check waits for the migration runner.
6. **Verify** the platform is serving from the restored server (place a read-only request, check
   App Insights `cloud_RoleInstance` is talking to the new DB).
7. **Decommission** the old/bad server only after a full validation window — keep it for forensics.

**Downtime:** the restore itself runs in the background (minutes to tens of minutes depending on
size; MVP DB ≤ 5 GB per ADR 0023 §2, so fast). The customer-visible outage is just the **re-point +
restart** window. Use a maintenance window.

⚠ **confirm against the live environment:** if the prod Postgres **Private Endpoint** cut-over has
landed (`postgres.bicep` `allowAllAzureServices` is staging-only; prod runs without it — see §C),
the restored server also needs its Private Endpoint / VNet rule wired **before** hosts can reach it.
Restoring a server does **not** copy firewall/PE config automatically.

## 2. Blob storage recovery

Blob holds product images, order attachments, invoices, labels, maker documents
(`blob.bicep` container map per ADR 0011).

### 2a. Soft-delete restore (target: 30-day, §7)

If soft-delete is enabled, a deleted/overwritten blob can be undeleted within the retention window:
```bash
az storage blob undelete \
  --account-name makablesprodblob \
  --container-name invoices \
  --name "<blob-path>"
```
Or, with blob **versioning** on, promote a prior version to current.

⚠ **confirm against the live environment / GAP:** `blob.bicep` does **not** configure a soft-delete
policy or versioning today. **Until the §7 cut-over lands, `az storage blob undelete` will fail —
there is nothing to undelete.** Enabling blob soft-delete (`az storage account blob-service-properties
update --enable-delete-retention true --delete-retention-days 30`) is a launch-checklist item (§C).
Treat current blob data as **not recoverable from accidental delete** until then.

### 2b. GRS / geo-failover (target: GRS in prod, §7)

GRS replicates the account to a paired region; on a regional outage you can initiate an
account failover.

⚠ **GAP:** `blob.bicep` ships `Standard_LRS` (local-redundant only) — **no geo-redundancy.** A
region loss in West Europe means blob data is unavailable until the region recovers; there is no
GRS failover target. Moving prod to `Standard_GRS` is a launch-checklist item (§C). At MVP scale
(≤ 50 GB, ADR 0023 §2) the re-upload-from-source cost of LRS is bounded but real for invoices/labels
that have no other source of truth.

### 2c. Re-generable vs. irreplaceable blobs

- **Re-generable:** invoice PDFs and shipping labels can be **re-rendered** from Postgres (the
  invoice row is the system of record; the PDF is a projection — see `Invoice.AttachPdfBlobPath`,
  set-once). If a PDF blob is lost but the DB row survives, re-render rather than restore.
- **Irreplaceable:** product images and maker documents have **no other source** — these are the
  blobs that most need soft-delete + GRS. Prioritize the §C blob cut-over for these containers.

## 3. Key Vault recovery

Deleted secrets are recoverable for 90 days (`key-vault.bicep` `softDeleteRetentionInDays: 90`):
```bash
az keyvault secret recover --vault-name kv-makables-weu-prod --name <secret-name>
```
A purged secret within the window is also recoverable unless purge-protection forced a hard purge.
⚠ **confirm:** purge-protection is **not** enabled in `key-vault.bicep` — consider enabling it
pre-launch so an attacker with vault rights can't hard-delete secrets.

---

## B. Admin-data restore considerations (the dangerous bits)

A Postgres PITR rewinds **all** tables together. Two table families make a naive rewind hazardous.

### B1. `admin_audit_log` — append-only, trigger-protected

The `admin_audit_log` table is enforced append-only at the **database** layer (migration
`20260523110529_OutboxAndAuditLog`): `BEFORE UPDATE` and `BEFORE DELETE` triggers
(`trg_admin_audit_log_reject_update` / `_reject_delete`) raise an exception via
`admin_audit_log_reject_modification()` (ADR 0014). Implications for restore:

- A **PITR rewind discards audit entries** written after the restore point. The audit chain is the
  legal record of admin actions — losing the tail means losing the record of everything operators did
  between the restore point and the incident. **Before a rewind, export the post-restore-point audit
  tail** (e.g. `COPY (SELECT * FROM admin_audit_log WHERE created_at > '<restore-time>') TO ...`)
  from the live server so the actions aren't silently erased.
- The **triggers come back with the table** on restore (they are schema objects in the same DB), so
  the restored server is still append-only — no special re-arming needed.
- If you must reconcile the exported tail back in, you cannot `INSERT` over the triggers' `UPDATE`/
  `DELETE` guards trivially — `INSERT` is allowed, so re-inserting the exported rows is the path
  (preserve original ids/timestamps). Do this as a deliberate, audited operation.

### B2. Invoices — immutable legal records + numbering sequence

`Invoice` is an immutable legal record (`Invoice.cs`: issuer/recipient/money/dates captured at
`Issue()` time, **immutable for the life of the row**; repository omits Update + Delete; errata go
through credit-notes post-MVP). The **invoice number is part of a legal sequence.** Implications:

- A **PITR rewind that lands before an invoice was issued "un-issues" that invoice number.** If the
  numbering counter also rewinds, the **next** issued invoice **reuses a number that a customer may
  already hold a PDF for** — a duplicate legal invoice number, which is a compliance violation.
  **This is the single most dangerous restore side-effect.**
- **Before any rewind that crosses an invoice issuance**, capture the **highest issued invoice number**
  from the live DB. After restore, confirm the numbering sequence will not re-emit an already-used
  number — if it would, advance the counter past the captured maximum **before** resuming traffic.
  ⚠ **confirm against the live environment:** the exact numbering-counter mechanism (DB sequence vs.
  per-country `CountryConfiguration` counter row) must be checked against the live schema and the
  invoice-numbering feature before the dry-run; this runbook flags the hazard but the precise
  "advance the counter" command depends on that mechanism.
- Invoice **PDF blobs** lost in the process are re-renderable from the (immutable) DB row — see §2c.

### B3. Outbox after restore

A rewind can resurrect already-processed outbox rows (`processed_at` set after the restore point is
lost) → they may **re-fire** (duplicate emails/labels). Outbox handlers are designed idempotent, but
after a restore, **spot-check the outbox** for rows that flipped back to unprocessed and decide
whether to acknowledge them rather than let them re-send (use the admin Acknowledge action — see
`monitoring.md` §4).

---

## C. ADR-divergence gaps (named, not papered over)

Per ADR 0023 §7 the production backup posture is 7-day PITR (met: 14 days shipped), **GRS blob**, and
**30-day blob soft-delete**. The shipped Bicep diverges on blob:

1. **Blob `Standard_LRS` → `Standard_GRS`** (`blob.bicep`). Closes via `docs/launch-checklist.md` →
   "Blob GRS (prod)".
2. **Blob soft-delete + versioning (30-day)** — not configured in `blob.bicep`. Closes via
   `docs/launch-checklist.md` → "Blob soft-delete 30-day".
3. **Key Vault purge-protection** — not enabled in `key-vault.bicep`. Recommended pre-launch.

Until (1)+(2) land, blob data is **LRS-only with no undelete** — flag this loudly in any incident.

---

## Verification (manual — staging / scratch dry-run) — **manual_step**

> Flagged as the ticket's `manual_step` (SecOps, pre-launch) and the ADR 0023 §7 quarterly restore
> test. The procedure is written now; an **actual restore on a scratch env** is the gate. **No
> production impact** — restores create new servers; the dry-run never touches prod.

1. **Postgres PITR dry-run (scratch):** run `az postgres flexible-server restore` against the staging
   server to a new `*-restored` server at a timestamp ~1 hour ago. Connect, confirm row counts on
   `orders` / `invoices` / `admin_audit_log` match expectations. Re-point a **scratch** host's conn
   string at it, restart, confirm it serves reads. Tear down the restored server.
2. **Blob soft-delete dry-run (scratch):** **first** enable soft-delete on the staging storage
   account (it is not on by default — this also validates the §C cut-over command). Delete a test
   blob, then `az storage blob undelete` it, confirm it returns.
3. **Audit-tail export drill:** `COPY`/`SELECT` the `admin_audit_log` rows newer than the chosen
   restore point off staging and confirm the export is complete before a (simulated) rewind.
4. **Invoice-numbering check:** capture the max issued invoice number on staging, confirm you can
   determine the live numbering-counter mechanism and how to advance it past that max (resolves the
   ⚠ in §B2).
5. **Time the recovery** end-to-end and record the actual RTO — feeds the "RTO/RPO formalized at
   5,000 orders/day" note in ADR 0023 §3.
6. Log findings (ADR 0023 §7 "Findings logged"). A failure blocks launch and re-opens this runbook;
   no production rollback occurs.
