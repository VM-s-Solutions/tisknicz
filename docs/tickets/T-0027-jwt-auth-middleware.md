---
id: T-0027
title: Per-host JWT auth middleware — audience binding for Customer / Maker / Admin / Public hosts
status: done
size: S
owner: dotnet-backend
created: 2026-05-24
updated: 2026-05-24
depends_on: [T-0021, T-0022]
blocks: [T-0035]
adrs: [0012]
phase: 2
---

# T-0027 — Per-host JWT auth middleware

## Scope

Wires the production `AddMakablesAuth` extension into every Web host with the per-audience policy from ADR 0012 §JWT structure, and pins the policy with end-to-end integration tests using `WebApplicationFactory<TProgram>` against each `Web.*` host's `Program`.

### Audience policy (ADR 0012 §JWT structure)
| Host | Accepted audiences |
|---|---|
| `Web.Customer` | `customer`, `admin` |
| `Web.Maker` | `maker`, `admin` |
| `Web.Admin` | `admin` only |
| `Web.Public` | `customer`, `maker`, `admin` (any authenticated caller) |

Admins can reach every host so they can act on behalf of any role; a customer JWT can never reach `Web.Maker` and vice versa.

### Config (`Makables.Config/Extensions/AddMakablesAuth.cs`)
- `services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))` so the binder is fed by every configuration source — including sources added AFTER `AddMakablesAuth` returns (notably `WebApplicationFactory.ConfigureAppConfiguration` in integration tests, which prepends sources during host build but doesn't influence eager reads inside this method).
- `JwtBearerOptions` are bound via `services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>>(...)`. The signing key + issuer + lifetime checks read the FINAL bound `JwtOptions`, so a missing key surfaces on the first protected request rather than at host build. This was the fix for the integration-test ordering problem (the old eager read fired before `ConfigureAppConfiguration` was applied).
- `TokenValidationParameters`:
  - `ValidateIssuer = true`; `ValidIssuer = jwt.Issuer`.
  - `ValidateAudience = true`; `ValidAudiences = AcceptedAudiencesFor(hostAudience)`.
  - `ValidateIssuerSigningKey = true`; HS256 over a base64-decoded key (must be ≥32 bytes).
  - `ValidateLifetime = true`; `ClockSkew = 30s` (tighter than .NET's 5-min default).
  - `MapInboundClaims = false` — keep `sub` / `email` / `role` wire-formatted.
  - `NameClaimType = ClaimTypes.NameIdentifier`; `RoleClaimType = "role"`.
- `AcceptedAudiencesFor(string hostAudience)` is the single source of truth for the policy table above. Unknown audience strings throw at startup.
- `HttpContextUserSessionProvider` implements `IUserSessionProvider` — reads `sub` / `email` / country-code claim off the inbound principal.

### Web hosts
Each host already calls `services.AddMakablesAuth(builder.Configuration, "<audience>")` in its `Program.cs`. The audience is host-local: `"customer"`, `"maker"`, `"admin"`, `"public"`. No host changes were needed for this ticket — the wiring was already in place from T-0015; T-0027 changes the binding mechanism and pins the policy.

### Integration tests (`Makables.IntegrationTests/Auth/JwtAuthMiddlewareTests.cs`)
14 facts using `WebApplicationFactory<TProgram>` against each `Web.*.Program`. Each test:
1. Boots the real host with `IntegrationTest` environment.
2. Injects in-memory `Jwt:*` config + a placeholder Postgres connection string + swaps the DbContext to SQLite in-memory.
3. Grafts a single `[Authorize]`-protected `GET /__test/protected` endpoint inline (Phase-2 controllers don't exist yet — we test the middleware, not endpoints).
4. Mints a real JWT via the production `JwtIssuer` with a deterministic 32-zero-byte test key.
5. Calls the endpoint and asserts the framework-authorized response.

Coverage:
- Customer host: accepts customer, accepts admin, **rejects maker**.
- Maker host: accepts maker, accepts admin, **rejects customer**.
- Admin host: accepts admin, rejects customer, rejects maker.
- Public host: theory over (customer, maker, admin) — all accepted.
- Cross-cutting: rejects token signed by a different key; rejects no token.

## Reviewer-prep notes
- The deferred-config pattern is documented in the file header so future agents don't "fix" it back to an eager read.
- No new domain code; everything lives under `Config/`. `Core.*` is untouched.
- No new packages.

## Acceptance criteria
- **AC-1** Build clean; 351 tests pass (274 unit + 77 integration).
- **AC-2** Every Web host enforces its audience policy end-to-end through the real `AddMakablesAuth` wiring against a real `JwtIssuer`-signed token.
- **AC-3** Cross-audience tokens (customer→maker, maker→customer) return 401.
- **AC-4** Admin tokens reach every host except the inverse-direction tests already cover; Admin host rejects everything else.
- **AC-5** Public host accepts any of customer/maker/admin.
- **AC-6** A token signed by a different key is rejected.
- **AC-7** A missing Authorization header returns 401.
- **AC-8** The deferred-config fix works: integration tests boot the host AND have their injected `Jwt:SigningKeyBase64` honored.

## Out of scope
- HTTP endpoints / controllers — T-0035.
- Per-role authorization policies beyond audience binding — handled per-endpoint in T-0035+.
- Refresh-token cookie wiring — T-0035.

## Status log
- 2026-05-24 done. 14 integration-test facts pass against all four Web hosts; full suite 351 green.
