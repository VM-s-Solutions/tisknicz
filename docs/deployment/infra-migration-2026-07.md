# Infra migration — Cleansia naming + Key Vault secrets (2026-07)

This migration renames every Azure resource to the Cleansia/CAF convention and
moves all secrets from plaintext App Settings to **Key Vault references**
resolved by managed identities (T-0134). It ships in one push to `master`, but
**four operator steps must happen first** — the deploy fails (loudly, by
design) without them.

## What changes when this lands

- **New resource names** = **new resources**. Azure cannot rename; the next
  deploy provisions `app-makables-customer-weu-dev`, `kv-makables-weu-dev`,
  `pg-makables-weu-dev`, `stmakablesweudev`, … alongside the old
  `makables-dev-*` set, which keeps existing but stops receiving deploys.
- **Dev Postgres starts empty.** `pg-makables-weu-dev` is a fresh server; the
  migrate job recreates the schema + seed. Old dev data stays on
  `makables-dev-db` until you delete it. (Dev data is disposable by design —
  if anything there matters, `pg_dump` it first.)
- **Secrets leave App Settings.** Hosts read `@Microsoft.KeyVault(SecretUri=…)`
  references; Bicep derives connection strings into the vault; the workflow
  pushes the external secrets. Nobody with mere Reader on the RG sees secret
  values anymore.
- **Functions storage goes identity-based** (`AzureWebJobsStorage__accountName`
  + `managedidentity`) — no account key in settings at all.
- **Container logs are ON** for every host (App Service Logs → filesystem), so
  the portal **Log stream** finally shows the app's console output.
- **Metric alerts** (Http5xx / latency / exceptions / Postgres) deploy when the
  `ALERT_EMAIL` GitHub secret is set.

## Before you push — operator steps (once)

1. **Create the new resource group** (the template does not create it):

   ```bash
   az group create --name rg-makables-weu-dev --location westeurope
   ```

2. **Grant the EXISTING GitHub OIDC principal rights on the new RG.** No new
   app registration is needed: federated credentials bind to the GitHub repo +
   Environment (`repo:…:environment:dev`), not to Azure resources, and the
   workflows keep the same Environment names — so the registration behind the
   current `AZURE_CLIENT_ID` secret keeps authenticating unchanged. Only the
   RBAC scope is new: the SAME principal needs Contributor **plus User Access
   Administrator** on the new RG (role-assignments.bicep creates RBAC grants;
   the push-secrets step self-grants Secrets Officer):

   ```bash
   # <AZURE_CLIENT_ID> = the client id already in the GitHub secrets
   SP_OID=$(az ad sp show --id <AZURE_CLIENT_ID> --query id -o tsv)
   RG_ID=$(az group show -n rg-makables-weu-dev --query id -o tsv)
   az role assignment create --assignee-object-id $SP_OID --assignee-principal-type ServicePrincipal --role Contributor --scope $RG_ID
   az role assignment create --assignee-object-id $SP_OID --assignee-principal-type ServicePrincipal --role "User Access Administrator" --scope $RG_ID
   ```

   Sanity checks: `az ad app federated-credential list --id <AZURE_CLIENT_ID> -o table`
   (subjects must include `environment:dev` / `environment:production`) and
   `az role assignment list --assignee <AZURE_CLIENT_ID> --all -o table`.
   If the SP already holds subscription-level Contributor, only the UAA grant
   is needed. The GitHub secrets (`AZURE_CLIENT_ID` / `AZURE_TENANT_ID` /
   `AZURE_SUBSCRIPTION_ID`) do not change. Do NOT share another project's app
   registration (e.g. Cleansia's) — per-project registrations keep a
   compromised workflow's blast radius to one project.

3. **Verify the GitHub Actions secrets** (Settings → Environments → `dev`).

   **Hard-required (deploy fails without them):** `AZURE_CLIENT_ID`,
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `POSTGRES_ADMIN_USER`,
   `POSTGRES_ADMIN_PASSWORD` (alphanumeric-only), `JWT_SIGNING_KEY_BASE64`
   (base64, ≥ 32 bytes — generate: `openssl rand -base64 32`).

   **Provider credentials — optional in dev:** `SENDGRID_API_KEY`,
   `COMGATE_MERCHANT_ID`, `COMGATE_SECRET`, `PACKETA_API_KEY`,
   `PACKETA_PUBLIC_WIDGET_KEY`, `MAPBOX_ACCESS_TOKEN`. When one is unset, the
   dev push step writes a non-secret **boot-stub** into Key Vault so the hosts
   boot (`ValidateOnStart` only needs non-empty; no host calls a provider at
   startup). Provider *calls* — payments, shipping, geocoding, **all emails
   (incl. registration confirmation)** — fail at call time until real values
   land. Setting the GitHub secret later overwrites the stub on the next
   deploy; a value set manually in the vault is never overwritten by an empty
   GitHub secret. **Production has no stub path — all secrets hard-required.**

   Optional: `ALERT_EMAIL` (enables metric alerts).

4. *(Recommended)* **What-if first** so the initial diff holds no surprises:

   ```bash
   export POSTGRES_ADMIN_USER=... POSTGRES_ADMIN_PASSWORD=...
   az deployment group what-if -g rg-makables-weu-dev \
     -f infra/bicep/main.bicep -p infra/bicep/envs/weu.dev.bicepparam
   ```

## Then push

Push to `master`. `deploy-staging.yml` runs: **provision** (all resources +
derived secrets + RBAC grants) → **push external secrets** → **migrate**
(schema onto the fresh Postgres) → **deploy** the 4 API hosts, Functions, and
the frontend.

## Verify after the run

1. Every job green.
2. Portal → each `app-makables-*-weu-dev` → Configuration: the KV-reference
   badges show **green** (resolved). A red badge on a *first* provision is
   usually RBAC propagation — restart the app after ~5 minutes.
3. `https://app-makables-customer-weu-dev.azurewebsites.net/` returns
   "Makables Customer API — alive." and App Insights (`appi-makables-weu-dev`)
   stops logging 503s on `robots933456.txt` (404 there = healthy).
4. Log stream now shows console output:
   `az webapp log tail -g rg-makables-weu-dev -n app-makables-customer-weu-dev`

## After verifying — cleanup + DNS

- **Rebind custom domains** (dev.makables.cz, dev-admin.makables.cz) from the
  old apps to the new ones + re-issue managed certs. Until then the new
  frontend answers on `web-makables-weu-dev.azurewebsites.net`.
- **Delete the old resources** (old RG `rg-makables-dev` contents:
  `makables-dev-*`, `makablesdevblob`, `makablesdevfn`) once traffic is on the
  new set — they bill until deleted. The old Key Vault `makables-dev-kv` is
  soft-deleted for 90 days after removal (its name stays reserved; irrelevant,
  the new vault has a different name).
- Copy any blobs worth keeping from `makablesdevblob` → `stmakablesweudev`
  (`azcopy` / portal) before deleting.

## Production

Same sequence with `rg-makables-weu-prod` + the `production` GitHub Environment
secrets, then dispatch `deploy-production.yml` (type `PRODUCTION`). Prod keeps
its stricter posture: no permanent Postgres firewall opening (break-glass rule
only during the migrate window), purge protection ON for the vault.
