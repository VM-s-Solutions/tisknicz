# Operations runbooks

Production operating procedures for the Makables platform (T-0134). Each runbook is grounded in the
**shipped** T-0016 Bicep infra (`infra/bicep/modules/*.bicep`), the real secret/option bindings
(`docs/deployment/env-vars.md` + the `*Options.cs` classes), and ADR 0023 §4 (observability) + §7
(deployment / secrets / backup). They are real procedures, not aspirational — where the shipped infra
diverges from the ADR target, the gap is named and pointed at `docs/launch-checklist.md`, not papered
over.

Each runbook ends with a **"Verification (manual — staging dry-run)"** section: the procedure is
written now; the dry-run that proves it is the SecOps pre-launch gate (the ticket's `manual_step`).

| Runbook | Use it when |
|---|---|
| [secret-rotation.md](./secret-rotation.md) | Rotating any secret (routine 90-day cycle or on suspected compromise): JWT signing key, Comgate / Packeta / SendGrid / Mapbox keys, the Postgres + Blob connection strings, and the Functions key. Covers provider rotation, the Key Vault update, host-pickup behavior (restart vs. auto-refresh), and blast radius. |
| [monitoring.md](./monitoring.md) | An alert fires (or you're on-call): the ADR 0023 §4 alert table — 5xx rate, webhook 5xx, outbox lag / stalled count, DB CPU, failed logins, auto-deliver crash — with the KQL to confirm, the likely cause, and the first-response action. |
| [go-live-bootstrap.md](./go-live-bootstrap.md) | Bringing a fresh environment (production especially) from "deployed and migrated" to "a customer can place an order": creating the first admin with `Makables.Tools.AdminBootstrap`, then the maker-registers → admin-verifies → product-created chain, then the first real Comgate walk. Contains an external party, so it is scheduled ahead of launch, not run on the day. |
| [backup-restore.md](./backup-restore.md) | Recovering data: Postgres PITR restore, Blob soft-delete / GRS recovery, plus the audit-log-append-only and invoice-immutability / numbering-sequence hazards a naive rewind triggers. Also the ADR 0023 §7 quarterly restore test. |

## Related security docs

- `docs/security/function-key-rotation.md` — the Functions key + poison-queue detail (referenced by
  secret-rotation §8 and monitoring §4).
- `docs/security/webhook-verification.md` — Comgate/Packeta origin + IP allowlist + status re-fetch
  (referenced by monitoring §2).
- `docs/security/rls-audit.md` — data-access posture.
- `docs/launch-checklist.md` — the pre-launch cut-overs the runbooks flag (secrets → Key Vault
  references, `AzureWebJobsStorage` → identity-based, Postgres Private Endpoint, Blob GRS + 30-day
  soft-delete).
