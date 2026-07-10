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

## Azure dev environment

The same six option groups must be present as **App Settings** on each Azure
Web App (via `infra/bicep/modules/app-service.bicep` + Key Vault references). A
503-on-warmup loop in a deployed environment almost always means one of these
settings is missing or a Key Vault reference is unresolved — check the App
Service **Log stream** for the `OptionsValidationException` naming the offending
key. See `docs/deployment/env-vars.md` for the Functions-host settings.
