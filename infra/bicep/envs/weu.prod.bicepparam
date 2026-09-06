using '../main.bicep'

// Production environment (resource group rg-makables-weu-prod) — same template
// as dev but with higher SKUs and the production domain as the only CORS origin.
// Naming follows the Cleansia/CAF convention: <type>-makables-<region>-<env>.
//
// SECRETS: only the Postgres admin pair is a Bicep parameter now. The
// application secrets are pushed into Key Vault by the deploy workflow's
// "Push external secrets" step and consumed as Key Vault references (T-0134).

param envSlug = 'prod'
param region = 'weu'
param location = 'westeurope'

// Postgres goes in northeurope, exactly as dev does. This is NOT a style
// choice: main.bicep defaults `postgresLocation` to `location`, and this
// subscription is offer-restricted for Postgres Flexible Server in westeurope
// (LocationIsOfferRestricted) — see the same note in weu.dev.bicepparam, which
// records westeurope and germanywestcentral as blocked and northeurope and
// francecentral as open. Omitting this param silently inherits westeurope, so
// the first production deploy would fail at server creation, before any app
// setting or networking is ever evaluated.
//
// The region is fixed at creation, so this must be right the FIRST time: a
// Flexible Server cannot be moved between regions, only dumped and restored.
// It also decides whether a future private endpoint is same-region or
// cross-region (Private Link supports both, but same-region is the simpler
// shape), so revisit this together with the VNet work if the restriction is
// ever lifted on this subscription.
param postgresLocation = 'northeurope'

// Per ADR 0023 §7: production runs General Purpose D2s_v3 Postgres and
// P1v3 App Service Plan. Burstable / P0v3 were a draft-time mistake that
// the T-0016 reviewer caught — both contradict the availability and
// CPU-alert assumptions in ADR 0023 §4.
param postgresSku = 'Standard_D2s_v3'
param postgresSkuTier = 'GeneralPurpose'
param postgresStorageGb = 64

param appServicePlanSku = 'P1v3'

// POSTGRES_ADMIN_USER / POSTGRES_ADMIN_PASSWORD come from GitHub Actions
// secrets at deploy time. There is intentionally NO fallback default for
// the password — readEnvironmentVariable without a default fails the
// deployment loudly if the secret is missing, which is what we want.
param postgresAdminUser = readEnvironmentVariable('POSTGRES_ADMIN_USER')
// Secureness is declared by @secure() on the param in main.bicep — decorators
// are not valid in a .bicepparam file (BCP130).
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')

param customerCorsOrigins = [
  'https://makables.cz'
]
param makerCorsOrigins = [
  'https://makables.cz'
]
param adminCorsOrigins = [
  'https://admin.makables.cz'
]
param publicCorsOrigins = [
  'https://makables.cz'
]

// Per-env non-secret app config.
param publicWebBaseUrl = 'https://makables.cz'
param jwtIssuer = 'https://makables.cz'

// Ops alert email — production should set the ALERT_EMAIL GitHub secret so
// Http5xx / latency / exceptions / Postgres alerts actually notify someone.
param alertEmail = readEnvironmentVariable('ALERT_EMAIL', '')

// --- Private network path -----------------------------------------------
// Production reaches Postgres over a private endpoint, never the public
// internet. There is deliberately NO "allow all Azure services" firewall rule
// here (main.bicep gates that to dev): with zero firewall rules the server is
// unreachable publicly, and with a private endpoint the apps still get in.
//
// publicNetworkAccess stays Enabled on the server. That is not a loophole —
// it is what lets the CI migrate job open a temporary runner-IP rule and
// delete it again. Disabling it would break migrations and discard the
// firewall rules anyway.
//
// AFTER THE FIRST PROD DEPLOY, confirm the private path actually took:
//   1. From a host's Kudu console, `nslookup
//      pg-makables-weu-prod.postgres.database.azure.com` must return a
//      10.20.2.x address. A public address means the endpoint or the DNS zone
//      link did not attach.
//   2. `az network private-endpoint-connection list` must show the connection
//      Approved. A Pending connection is the one failure mode ARM reports as
//      success — auto-approval needs the deploy principal to hold
//      .../privateEndpointConnectionsApproval/action, which Owner covers.
// The smoke job's DB-backed probe also fails closed if the path is broken
// (with zero firewall rules there is no public fallback), so this is a
// belt-and-braces check rather than the only detector.
param enablePrivateNetworking = true

// --- Comgate -----------------------------------------------------------
// Production has NO dev payment bypass — the keyed 'dev' provider is not
// registered at all — so both of these are load-bearing here.
//
// COMGATE_BASE_URL: empty keeps the code default (the live gateway), which
// is correct for production. Set it only to pin the value explicitly.
param comgateBaseUrl = readEnvironmentVariable('COMGATE_BASE_URL', '')

// COMGATE_WEBHOOK_ALLOWED_IPS: comma-separated IPs / CIDR ranges from
// Comgate's published list. REQUIRED before go-live — the allowlist is
// fail-closed, so while this is empty every payment callback is rejected
// with 401 and no order can ever reach Paid.
param comgateWebhookAllowedIps = empty(readEnvironmentVariable('COMGATE_WEBHOOK_ALLOWED_IPS', '')) ? [] : split(readEnvironmentVariable('COMGATE_WEBHOOK_ALLOWED_IPS', ''), ',')
