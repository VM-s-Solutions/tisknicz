# Local development — running the backend

How to run the Makables backend on your machine and why it needs the config
it needs. If the frontend shows **"Server je momentálně nedostupný"** or login
returns **`ERR_CONNECTION_REFUSED`**, the backend host is not running — start
it with the steps below.

## Prerequisites

| Dependency | How |
|---|---|
| .NET 10 SDK | `dotnet --version` → `10.x` |
| Postgres 16 on `localhost:5432` | db `makables_dev`, user/pass `postgres`/`postgres` (matches `appsettings.Development.json`) |
| Azurite (blob + queue) — optional | Only order-attachment upload and outbox dispatch touch it; the hosts boot without it |

A Postgres container exposing `127.0.0.1:5432` and an Azurite container are the
simplest way to get both. Verify Postgres is up before starting the hosts:

```powershell
Test-NetConnection 127.0.0.1 -Port 5432 -InformationLevel Quiet   # must be True
```

## Run all four API hosts

```powershell
pwsh scripts/run-dev.ps1            # starts all four hosts, each in its own window
pwsh scripts/run-dev.ps1 -Build     # build the solution first
pwsh scripts/run-dev.ps1 -Host Customer   # just one host
```

| Host | URL | Serves |
|---|---|---|
| Customer | http://localhost:5001 | customer app (login, orders, checkout) |
| Maker | http://localhost:5002 | maker dashboard |
| Admin | http://localhost:5003 | admin ops |
| Public | http://localhost:5104 | catalog, product pages, Comgate webhook |

Each host also exposes `/openapi/v1.json`. To run a single host by hand:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/src/Makables.Web.Customer --launch-profile http
```

The frontend (`cd frontend; npm run dev`) defaults to exactly these ports, so no
`NEXT_PUBLIC_API_*_BASE_URL` env vars are required for local dev.

## Seed dev/test data

`Makables.Tools.Seeder` builds a realistic CZ dataset on top of the reference
data the migrations already seed (country CZ, CountryConfiguration, the six
launch categories). It runs in **two passes**, both inside one transaction:

1. **Demo snapshot** (`DevDataSeeder.cs`) — 10 users (1 admin, 4 customers,
   5 makers), 5 makers (4 verified + 1 in the admin verification queue), 15
   products across every category (including one `OnRequest`, one soft-deleted,
   one from the unverified maker), 14 orders covering **every** `OrderState`,
   message threads with unread counters, 4 reviews, and one open dispute.
2. **Catalog makers** (`DevDataSeeder.CatalogMakers.cs`) — 50 more makers
   across the whole country (Praha and Brno repeat so the city filter has
   partial matches), each with 2–4 products and 0–7 completed orders, ~105
   reviews with replies, and recomputed catalog stats. Enough to exercise
   paging, the city / category / rating filters and the sort order on
   `/katalog`.

```powershell
dotnet run --project backend/src/Makables.Tools.Seeder                 # seed / top up (no-op when everything exists)
dotnet run --project backend/src/Makables.Tools.Seeder -- --reset      # delete seed-* rows and reseed
dotnet run --project backend/src/Makables.Tools.Seeder -- --migrate    # apply pending migrations first
```

- **Credentials:** every seeded account uses the password `SeedHeslo.123`.
  Admin: `admin@makables.test`; customers: `jana.novakova@` / `petr.svoboda@` /
  `eva.dvorakova@` / `tomas.marek@` (unconfirmed) `makables.test`; makers:
  `karel.tiskar@` (PrintLab), `marie.vltavska@` (Tiskárna Vltava),
  `ondrej.barvir@` (Textilka Brno), `lucie.rezava@` (LaserCut Ostrava),
  `alena.lipova@` (Dílna U Lípy, unverified) `makables.test`. The catalog
  makers own `{jmeno}.{prijmeni}@makables.test` accounts derived from the
  owner name (`jan.dvorak@makables.test` for Praha3D Studio).
- **Idempotent, and safe to run against a dev database that already has
  data.** All ids are deterministic (`seed-*`). Pass 1 is all-or-nothing
  behind the `seed-user-admin` sentinel; pass 2 is checked **per maker** —
  a blueprint whose maker id, slug, IČO, user id or e-mail is already taken
  is left alone, so adding a 51st blueprint later inserts exactly that one.
  Nothing is ever duplicated and nothing existing is overwritten.
- **Connection string:** the local default lives in the seeder's
  `appsettings.json`, but `dotnet run` uses the shell's working directory as
  the content root — from the repo root pass it explicitly:
  `$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=makables_dev;Username=postgres;Password=postgres'`.
- **Safety:** refuses non-local hosts unless `--allow-remote`, and always
  refuses any host/database whose name contains `prod`.
- **Order numbers** use the reserved `M-CZ-{YYYY}9NNN` range so the live
  `IOrderNumberGenerator` sequence (which starts at `0001`) cannot collide
  with them in dev.
- The connection string comes from the seeder's `appsettings.json`
  (`makables_dev` on localhost) and can be overridden via
  `ConnectionStrings__Postgres`.

## Why the hosts need stub secrets to boot

Every host wires several option groups with `.ValidateOnStart()` — `Jwt`,
`SendGrid`, `Mapbox`, `Comgate`, `Packeta`, `BlobStorage`, `OutboxQueues`. If any
required value is missing, the host throws `OptionsValidationException` at
startup **and never binds its port**. In Azure this shows up as a continuous
**503 on the `GET /robots933456.txt` warmup probe** (App Service's health ping
against an app that failed to start); locally it shows up as
`ERR_CONNECTION_REFUSED`.

To keep local dev working out of the box, each host's
`appsettings.Development.json` carries **non-secret placeholder values** for
these groups (the same public stubs CI uses in the spec-parity job). They only
need to *pass the validators* — no host calls Comgate/Packeta/Mapbox/SendGrid at
startup. Real integration credentials are never committed; supply them via
user-secrets or a git-ignored `appsettings.Development.local.json` if you need to
exercise a live provider locally.

> The JWT `SigningKeyBase64` stub decodes to exactly 32 bytes, the minimum the
> `JwtOptionsValidator` accepts. It is a dev-only placeholder — tokens signed
> with it are not valid against any real environment.

## Paying without Comgate (dev payment bypass)

The Comgate stubs above pass the validator but cannot create a real payment
session, so checkout would dead-end. `Payments:Dev` swaps the gateway for
`DevPaymentProvider`, which turns **Zaplatit** into a one-click pay:

```jsonc
"Payments": {
  "Dev": {
    "Enabled": true,
    "ConfirmBaseUrl": "http://localhost:5001"   // Customer host
  }
}
```

It is already set in every host's `appsettings.Development.json`, and the
deploy template sets it on the Azure **dev** environment only
(`envSlug == 'dev'` in `infra/bicep/main.bicep`).

**How it flows.** `CreatePaymentSession` hands back a redirect URL pointing at
`GET /api/v1/orders/{orderId}/dev-payment/confirm` on the Customer host instead
of a Comgate page. Following it dispatches the ordinary `MarkOrderPaid` command
and bounces the browser to `/objednavka/{id}`. Everything downstream is
identical to a real payment — order state, outbox emails, invoice generation,
audit trail. The adapter itself never touches the database.

**Why it cannot leak into production:**

| Guard | Effect |
|---|---|
| `Payments:Dev:Enabled` defaults to `false` | An environment that omits the section keeps Comgate. |
| Bicep gates on `envSlug == 'dev'` | The setting is structurally absent from production, not merely `false`. |
| Provider registered only when enabled | On production the keyed `dev` service does not exist at all. |
| Confirm endpoint 404s when disabled | The route answers as if it were a typo. |
| Refs are prefixed `dev-` | The confirm endpoint refuses any order whose reference a real gateway issued. |

It fails **loud**, never silent: `Enabled=true` with the provider missing
returns `payment.providerNotRegistered` rather than falling back to charging a
real card, and `Enabled=true` without a valid `ConfirmBaseUrl` crashes the host
at boot.

> On deployed environments `ConfirmBaseUrl` is the **origin-relative**
> `/api-proxy/customer`, not an absolute URL. The session cookies are
> `SameSite=Strict`, so the confirm hop has to stay same-origin; leaving it
> relative means the browser resolves it against whichever hostname the tester
> actually browsed. Locally the absolute `http://localhost:5001` works because
> ports do not split a site.

## Azure dev environment

The same six option groups must be present as **App Settings** on each Azure
Web App (via `infra/bicep/modules/app-service.bicep` + Key Vault references). A
503-on-warmup loop in a deployed environment almost always means one of these
settings is missing or a Key Vault reference is unresolved — check the App
Service **Log stream** for the `OptionsValidationException` naming the offending
key. See `docs/deployment/env-vars.md` for the Functions-host settings.
