# Runbook — Secret rotation

> **Scope:** every secret the Makables platform reads at runtime, how to rotate it at the
> provider, how to update it in Key Vault, and how the App Services / Functions pick the new
> value up. Grounded in the shipped T-0016 Bicep (`infra/bicep/modules/*.bicep`) and the real
> option bindings (the `*Options.cs` classes). See ADR 0023 §7 (Secrets) for the target posture.
>
> **Owner:** SecOps. **Cadence:** routine rotation every 90 days; immediate on suspected
> compromise. **Audience:** an operator with `Key Vault Secrets Officer` on the prod vault and
> `Contributor` on the resource group.

## 0. The boot-time safety net (read this first)

The platform binds its critical secrets through `services.AddOptions<T>().Validate(...).ValidateOnStart()`.
A bad or missing secret therefore **crashes the host at boot**, not at the first customer request:

- `JwtOptions` — validated by `JwtOptionsValidator.IsValid` in `AddMakablesAuth.cs`
  (`Jwt:Issuer` non-empty, `Jwt:SigningKeyBase64` decodes to ≥ 32 bytes,
  `AccessTokenLifetime > 0`).
- `AzureBlobStorageOptions` — `ValidateOnStart` requires either `ConnectionString` or `ServiceUri`.
- `PacketaOptions`, `PublicAppUrlsOptions`, `SendGrid`/`Email` options — validated at startup.
- Functions timer schedules (`%ProcessOutbox:Schedule%` etc.) — a missing key fails function
  indexing at host startup (no in-code fallback; see `docs/deployment/env-vars.md`).

**Operational consequence:** rotate into a deployment **slot** (or staging) first and confirm the
host boots. A botched rotation surfaces as a failed deploy / failed slot warm-up, which is exactly
where you want to catch it. The production App Service plan supports slot swap per ADR 0023 §7
(easy rollback = swap back).

## A. Secret inventory (one row per secret)

| # | Secret | Option binding | KV secret (target) | Provider |
|---|---|---|---|---|
| 1 | JWT signing key | `Jwt:SigningKeyBase64` (`JwtOptions`) | `jwt-signing-key` | self-generated |
| 2 | Comgate shared secret | `Comgate:Secret` (`ComgateOptions`) | `comgate-secret` | Comgate portal |
| 3 | Packeta API key | `Packeta:ApiKey` (`PacketaOptions`) | `packeta-apikey` | Packeta client zone |
| 3b | Packeta public widget key | `Packeta:PublicWidgetKey` (`PacketaOptions`) | `packeta-widgetkey` | Packeta client zone |
| 4 | SendGrid API key | `SendGrid:ApiKey` (`SendGridOptions`) | `sendgrid-apikey` | SendGrid dashboard |
| 5 | Mapbox access token | `Mapbox:AccessToken` (`MapboxOptions`) | `mapbox-token` | Mapbox account |
| 6 | Postgres conn string | `ConnectionStrings__Postgres` | `postgres-connstring` | Postgres Flexible Server |
| 7 | Blob/queue conn string | `AzureWebJobsStorage`, `OutboxQueues:ConnectionString` | `storage-connstring` | Storage account key |
| 8 | Functions key | `x-functions-key` (ProcessOutbox, run-batch) | `functions-processoutbox-key` | Functions host |

> ⚠ **confirm against the live environment.** The KV secret *names* above are the planned naming
> convention — secrets are **not yet** Key Vault references in the shipped Bicep (they are plain App
> Settings on each App Service / Functions host; see `infra/bicep/modules/app-service.bicep` and
> `functions.bicep`). The cut-over to `@Microsoft.KeyVault(SecretUri=...)` references is the
> `TODO(T-0134)` in `main.bicep` and is tracked as a launch-checklist item (see §C). Until that
> cut-over lands, "update the Key Vault secret" below means "update the App Setting in the portal /
> via `az webapp config appsettings set`, then restart". After cut-over, it means `az keyvault
> secret set` + auto-refresh.

### How App Services pick up a new value

- **Plain App Setting (current state):** changing an App Setting **restarts** the App Service
  automatically. No auto-refresh — the new value is live after the restart completes.
- **Key Vault reference (target state):** App Service refreshes resolved Key Vault references on a
  background cadence (Azure documents ~30 min, not guaranteed). For a deterministic pickup, **restart
  the App Service** (`az webapp restart`) after `az keyvault secret set` rather than waiting.
- **Adapters that cache `IOptions<T>.Value` at first resolve** (named `HttpClient` factories for
  Mapbox, Comgate, Packeta, SendGrid; the singleton `QueueClient` for outbox queues) **do not pick
  up a config refresh** — they require a host restart. This is already documented for Mapbox and the
  storage conn string in `docs/security/function-key-rotation.md`. Treat **restart as mandatory**
  for secrets 2–7.

---

## 1. JWT signing key — `Jwt:SigningKeyBase64`

The signing key is HMAC-SHA256 (`JwtOptions`, validated ≥ 32 bytes). `JwtOptions.KeyId` defaults
to `"k1"` and is stamped as the `kid` on every issued token (`AddMakablesAuth.cs` sets
`SymmetricSecurityKey.KeyId = jwt.KeyId`).

**The tradeoff (decision required, made explicit here):** the shipped validation wiring registers a
**single** `IssuerSigningKey` — there is no dual-key validation path today. Therefore, at MVP the
accepted behavior is **"all active sessions drop on rotation"**:

- Access tokens (15-min lifetime) signed with the old key fail signature validation immediately
  after the new key goes live → `401`.
- **Refresh tokens are unaffected by signing-key rotation** — they are opaque DB-backed tokens
  (`RefreshToken` entity), not JWTs. A `401` on the access token triggers the frontend's
  `401 → refresh → retry` flow (`lib/runtime/api-fetch.ts`), which mints a **new** access token
  signed with the new key. So in practice, **most users see no logout** — the next API call
  silently refreshes. Users whose refresh token is also mid-rotation or expired get a forced
  re-login. **Customer impact: brief; bounded by the 15-min access-token window.**

**Procedure:**
1. Generate a new 32-byte key: `openssl rand -base64 32` (or `[Convert]::ToBase64String((1..32 | % {Get-Random -Max 256}))` in PowerShell).
2. Set it in the prod vault: `az keyvault secret set --vault-name makables-prod-kv --name jwt-signing-key --value "<base64>"` (or update the App Setting until KV-ref cut-over).
3. **Restart all four Web hosts** (`makables-prod-customer/-maker/-admin/-public`) and the Functions
   host. `ValidateOnStart` re-validates the new key at boot — a malformed base64 or a < 32-byte key
   **fails the boot**, so you catch a bad rotation before it serves a request.
4. **Blast radius:** all hosts (the key is shared). Sub-15-min window of access-token churn handled
   by silent refresh. **Downtime:** none if rotated via slot swap; a few seconds of per-host restart
   otherwise.

> **Future dual-key grace window (not shipped):** `JwtOptions.KeyId` + the `kid` claim are already
> wired precisely so a later ADR can move to `k1 → k2` with both keys accepted during the window.
> When that lands, this section gets a "zero-logout rotation" variant. Today, do NOT assume it works.

## 2. Comgate secret — `Comgate:Secret`

Used for the request `secret` field and webhook signature verification (`ComgateOptions`). NEVER
logged, NEVER in URLs (enforced in the adapter).
1. Regenerate at the Comgate merchant portal (account settings → API/webhook secret).
2. `az keyvault secret set --name comgate-secret --value "<new>"`.
3. **Restart** the Public host (it owns the Comgate webhook + payment-create calls). The named
   `HttpClient`/options are cached at resolve.
4. **Blast radius:** payment creation + webhook verification. **Downtime:** rotate during a low-order
   window; in-flight payments mid-rotation may need a Comgate retry. Comgate retries non-2xx webhooks
   (ADR 0023 §3), so a transient verification miss self-heals.

## 3. Packeta API key — `Packeta:ApiKey` (+ `Packeta:PublicWidgetKey`)

Private `ApiKey` is Packeta's `apiPassword`, sent as a form field on every REST call (`PacketaOptions`,
validated by `PacketaOptionsValidator` at `ValidateOnStart`). `PublicWidgetKey` is the frontend
widget key.
1. Regenerate in the Packeta client zone (API settings).
2. `az keyvault secret set --name packeta-apikey --value "<new>"` (and `packeta-widgetkey` if rotated).
3. **Restart** the Maker host (label creation) and the Functions host (`SyncShipmentStatuses`,
   `GenerateLabel`). The widget key also flows to the frontend via the public widget-config endpoint —
   restart the Public host so it serves the new key.
4. **Blast radius:** label generation + shipment-status sync. A bad key fails boot (`ValidateOnStart`).
   **Downtime:** none for browsing; label generation pauses until restart completes.

## 4. SendGrid API key — `SendGrid:ApiKey`

`SendGridOptions`. Outbound transactional email.
1. Create a new API key in the SendGrid dashboard (Settings → API Keys), **then** delete the old one.
2. `az keyvault secret set --name sendgrid-apikey --value "<new>"`.
3. **Restart** the Functions host (`SendEmailFunction` is the only sender). Web hosts do not send
   email directly.
4. **Blast radius:** email delivery only. A failed send **does not lose data** — the outbox row
   records the failure and retries (`OutboxEvent.RecordFailure`), so a brief gap during rotation
   self-heals on the next `ProcessOutbox` tick. **Downtime:** none customer-visible.

## 5. Mapbox access token — `Mapbox:AccessToken`

`MapboxOptions`. Server-side only (the frontend never calls Mapbox; ADR 0010). Sent as
`Authorization: Bearer` so it never lands in OTel span attributes (see `function-key-rotation.md`).
1. Rotate the token in the Mapbox account dashboard (Access tokens).
2. `az keyvault secret set --name mapbox-token --value "<new>"`.
3. **Restart** the Customer + Maker hosts (they own the autocomplete proxy). The named `HttpClient`
   caches `IOptions<MapboxOptions>.Value` at first resolve — config refresh alone won't pick it up.
4. **Blast radius:** address autocomplete/geocoding. **Downtime:** none; degrades to no-suggestions
   for a few seconds during restart.

## 6. Postgres connection string — `ConnectionStrings__Postgres`

Injected by Bicep from the Postgres module output (`main.bicep`, `app-service.bicep`,
`functions.bicep`). **This is the highest-blast-radius secret.**
1. Rotate the admin password: `az postgres flexible-server update --resource-group <rg> --name makables-prod-pg --admin-password "<new>"`.
2. `az keyvault secret set --name postgres-connstring --value "Host=...;Database=makables;Username=...;Password=<new>;SslMode=Require"`.
3. **Restart all four Web hosts + the Functions host.** `Web.Customer` is the migration runner; the
   other hosts wait for it on a readiness check (ADR 0023 §7) — restart Customer first, confirm
   healthy, then the rest.
4. **Blast radius:** the entire platform (every host talks to Postgres). **Downtime:** rotate via slot
   swap to minimize; otherwise expect a short outage window across hosts. Do this in a maintenance
   window. ⚠ **confirm against the live environment:** if the prod Private Endpoint cut-over (see §C)
   has landed, the conn string host is the private FQDN, not the public one.

## 7. Blob / queue connection string — `AzureWebJobsStorage` / `OutboxQueues:ConnectionString`

Injected by `functions.bicep` from `storage.listKeys()`. Powers the Functions runtime, the outbox
handoff queues, and (via `AzureBlobStorageOptions.ConnectionString` in non-prod) blob access.
1. Rotate the storage account key: `az storage account keys renew --account-name makablesprodfn --key key1`.
2. `az keyvault secret set --name storage-connstring --value "<new conn string with key1>"`.
3. **Restart the Functions host** — the singleton `QueueClient` is built once at boot
   (`function-key-rotation.md`); a config reload won't re-create it.
4. **Blast radius:** all background jobs + outbox handoff. Outbox is durable (DB-backed) so no event
   is lost — processing pauses until restart, then drains. **Downtime:** background only; no
   customer-facing impact. **Two-key trick:** renew `key1` while `key2` is live (or vice-versa) to
   avoid a window where no key is valid.
5. ⚠ **confirm against the live environment:** the `TODO(T-0134)` in `functions.bicep` is to move
   `AzureWebJobsStorage` to an **identity-based** connection (`AzureWebJobsStorage__accountName` +
   managed-identity role) so there is no account key to rotate at all. After that cut-over, this row
   becomes "grant/rotate the role assignment", not "renew the key" (see §C).

## 8. Functions key — `x-functions-key`

Protects `POST /api/outbox/process` (`ProcessOutboxFunction`, `AuthorizationLevel.Function`) and
`POST /api/payouts/run-batch`. Full procedure already documented in
`docs/security/function-key-rotation.md` (primary/secondary swap, 90-day cadence, KV secret
`functions-processoutbox-key`). Summary: rotate in the portal (Function App → Functions → Function
Keys → Rotate), update the KV secret, re-deploy the admin dashboard consumer. The primary/secondary
pattern gives **zero-downtime** rotation. **Blast radius:** the admin "Process outbox now" / payout
escape-hatches only.

---

## C. ADR-divergence gaps (named, not papered over)

Per ADR 0023 §7 the production target is Key-Vault-referenced secrets, identity-based storage, and a
private Postgres path. The shipped Bicep diverges; these are pre-launch cut-overs, NOT this runbook:

1. **Plain App Setting → Key Vault reference** for the Postgres conn string and the rest
   (`TODO(T-0134)` in `main.bicep`). Closes via `docs/launch-checklist.md` → "Secrets to Key Vault
   references".
2. **`AzureWebJobsStorage` → identity-based connection** (`TODO(T-0134)` in `functions.bicep`).
   Closes via `docs/launch-checklist.md` → "AzureWebJobsStorage identity-based".
3. **Postgres Private Endpoint** replacing the staging "allow all Azure services" firewall rule
   (`postgres.bicep`, `allowAllAzureServices`). Closes via `docs/launch-checklist.md` → "Postgres
   Private Endpoint (prod)".

Until (1) lands, treat "update the Key Vault secret" as "update the App Setting + restart".

---

## Verification (manual — staging dry-run) — **manual_step**

> Flagged as the ticket's `manual_step` (SecOps, pre-launch). The procedure above is written now; the
> dry-run that proves it is the launch gate. **Staging-only — no production impact.**

1. Pick **one non-critical secret** on staging — recommended **SendGrid API key** (failed sends are
   recoverable via outbox retry, so a botched dry-run can't break customer flows).
2. Generate a new key at SendGrid, `az keyvault secret set` (or App Setting) on the **staging** vault,
   restart the **staging** Functions host.
3. Confirm the host **boots clean** (ValidateOnStart passes) — check App Insights for a successful
   startup trace, no boot exception.
4. Trigger an email path (e.g. a test order on staging) and confirm delivery with the new key.
5. **Negative test:** set a deliberately malformed `Jwt:SigningKeyBase64` (e.g. `"not-base64"`) on a
   staging slot and confirm the host **refuses to boot** (`JwtOptionsValidator` rejects it) — this
   proves the safety net. Revert immediately.
6. Log the dry-run outcome. A failure blocks launch and re-opens this runbook; it does not roll back
   anything in production.
