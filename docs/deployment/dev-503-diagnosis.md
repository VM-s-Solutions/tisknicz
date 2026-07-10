# Azure dev environment — the `GET /robots933456.txt` 503 loop

**Symptom:** App Insights for the dev environment logs a continuous stream of
**503** responses on `GET /robots933456.txt`, alongside thousands of
`Exception` / `InvalidOperationException` entries.

**Root cause (confirmed):** `robots933456.txt` is Azure App Service's built-in
**warmup / health-check probe path**. A 503 there means the app process
**failed to start** and never bound its port, so every probe hits a dead site.
The `InvalidOperationException` flood is the startup crash itself.

The Makables Web hosts wire seven option groups with `.ValidateOnStart()` —
`Jwt`, `BlobStorage`, `SendGrid`, `Mapbox`, `Ares`, `Comgate`, `Packeta`. If any
**required** value is missing or blank, the host throws
`OptionsValidationException` at build and the process exits. (`Ares` has a
default `BaseUrl`, and Google/Apple OAuth are deliberately *not*
`ValidateOnStart`, so those never block boot.)

This was reproduced locally: with the provider settings absent the Customer host
died with exactly this exception set (`Jwt configuration invalid …`,
`SendGrid:ApiKey is required.`, `Mapbox:AccessToken is required.`,
`Comgate:MerchantId is required.; Comgate:Secret is required.`,
`Packeta:ApiKey is required.; Packeta:PublicWidgetKey is required.`,
`BlobStorage requires either ConnectionString or ServiceUri.`); once the values
were supplied it bound `:5001` and served normally.

## Why it's the dev environment specifically

Since T-0134 the hosts read secrets as **Key Vault references** resolved by
their managed identities: Bicep writes the derivable secrets (connection
strings) into `kv-makables-weu-dev`, and `deploy-staging.yml`'s **"Push external
secrets to Key Vault"** step pushes the rest (JWT key, SendGrid, Comgate,
Packeta, Mapbox) from GitHub Environment secrets.

So the app crashing on `ValidateOnStart` in dev means one of:

1. **A required GitHub Actions secret is unset/empty**, so the push step failed
   (it fails loudly on any empty required secret) and the Key Vault secret is
   missing — the KV reference resolves to nothing and the validator rejects it;
2. **The deploy never completed successfully**, so the App Services carry no
   (or stale) settings and the old failing process keeps 503ing; or
3. **RBAC propagation lag on a first provision** — the host started before its
   Key Vault Secrets User grant propagated. This self-heals; restart the app
   after a few minutes if the KV references still show as unresolved.

## Fix (operational — no code change)

1. **Confirm the deploy actually ran green.** Check the latest
   `deploy-staging.yml` run — both the `Deploy Bicep` and the
   `Push external secrets to Key Vault` steps must pass. Both are fail-closed:
   a missing secret aborts the run rather than deploying blanks.

2. **Verify every required GitHub Actions secret is set** (repo/environment
   secrets consumed by `deploy-staging.yml`):

   | Secret | Becomes Key Vault secret | Validator that fails if blank |
   |---|---|---|
   | `JWT_SIGNING_KEY_BASE64` | `Jwt--SigningKeyBase64` | must base64-decode to ≥ 32 bytes |
   | `SENDGRID_API_KEY` | `SendGrid--ApiKey` | non-empty |
   | `MAPBOX_ACCESS_TOKEN` | `Mapbox--AccessToken` | non-empty |
   | `COMGATE_MERCHANT_ID` | `Comgate--MerchantId` | non-empty |
   | `COMGATE_SECRET` | `Comgate--Secret` | non-empty |
   | `PACKETA_API_KEY` | `Packeta--ApiKey` | non-empty |
   | `PACKETA_PUBLIC_WIDGET_KEY` | `Packeta--PublicWidgetKey` | non-empty |
   | `POSTGRES_ADMIN_USER` / `POSTGRES_ADMIN_PASSWORD` | (Bicep derives `ConnectionStrings--Postgres`) | deploy aborts if blank |
   | `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | — (OIDC login) | deploy can't auth |

   For dev you may use non-production placeholder values that merely pass the
   validators (e.g. a real base64 32-byte JWT key, stub SendGrid/Mapbox/Comgate/
   Packeta strings) — no host calls those providers at startup. Only the JWT key
   must be a genuine ≥32-byte base64 value.

3. **Read the App Service Log stream to confirm.** In the Azure Portal, open each
   `app-makables-*-weu-dev` Web App → **Log stream** (container logging is now
   enabled by Bicep, so the console output is actually there). A boot crash
   prints the `OptionsValidationException` naming the exact missing key. Also
   check Configuration → the KV-reference badges: a red badge = the reference
   didn't resolve (missing secret or missing RBAC grant).

4. **Re-run `deploy-staging.yml`** (push to `master` or dispatch it). A green run
   provisions + pushes secrets; the hosts boot; the warmup probe starts returning
   404 (healthy — there is no literal `robots933456.txt`, and App Service treats
   any non-5xx as "warm"), and the 503 loop stops.

## Related

- `docs/deployment/local-dev.md` — the same validators, the local stub config.
- `docs/deployment/env-vars.md` — the Functions-host app settings.
- `docs/deployment/deploy-runbook.md` — the full deploy procedure.
