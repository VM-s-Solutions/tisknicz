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

## Reviewer findings and resolutions (commit 5aca948 → follow-up)

Two reviewers ran in parallel.

### Security reviewer — BLOCKER × 1 + MAJOR × 4

- **B-1 Public host accepts any audience** — silent footgun: a future `[Authorize]`-protected Public endpoint would let a maker token reach a customer-intended route. **Fixed (contract):** The Public policy stays permissive (Public IS the anonymous + any-authenticated surface), but the contract is now loud: file header + ADR 0012 + this ticket all say "protected endpoints on Public MUST mount a named policy that checks `role` / `aud` explicitly — bare `[Authorize]` on Public is not enough." T-0035+ controllers will enforce this. Pinned by the `MapInboundClaims_is_off_so_sub_claim_is_present_verbatim` fact which proves the `role` claim is round-tripped wire-formatted so policy authors can read it.
- **M-1 Missing negative tests** — **Fixed:** added five facts: expired-token, wrong-issuer, `alg=none` unsigned, malformed Bearer payload, and `MapInboundClaims=off` claim round-trip. Plus `ValidAlgorithms = [HmacSha256]` is now explicit on the validation parameters (defense-in-depth against algorithm-confusion).
- **M-2 Deferred config defers a deploy-blocking failure** — **Fixed:** `services.AddOptions<JwtOptions>().Validate(JwtOptionsValidator.IsValid).ValidateOnStart()` now crashes the host at boot if `Issuer` / `SigningKeyBase64` / `AccessTokenLifetime` is missing or the key is shorter than 32 bytes. Host-startup failure is observable in deploy logs; misconfigs cannot survive past the first protected request anymore.
- **M-3 `RequireHttpsMetadata = false` misleading comment** — **Fixed:** comment rewritten to spell out that the flag is moot for HS256 (no metadata endpoint is fetched) and inbound-request HTTPS is enforced by the ingress proxy.
- **M-4 `HttpContextUserSessionProvider` silently returned null** — **Fixed:** `GetUserId()` now throws if the principal is authenticated and `sub` / `NameIdentifier` is missing. Fails closed at the boundary.

### Code-quality reviewer — 0 BLOCKERs + 2 MAJORs

- **M-1 `"public"` magic string** — **Fixed:** added `MakablesHosts` constants (`Customer`, `Maker`, `Admin`, `Public`). `AcceptedAudiencesFor` and `AddMakablesRateLimiting` both switch on the constants; every `Web.*/Program.cs` now declares `const string Audience = MakablesHosts.<Host>`.
- **M-2 ADR-table drift risk** — **Fixed:** ADR 0012 §JWT structure now says explicitly: "The runtime source of truth for the per-host audience table is `MakablesAuthExtensions.AcceptedAudiencesFor` and is pinned by `JwtAuthMiddlewareTests`. If this narrative ever disagrees with that method, the method wins." Sources of truth collapsed.
- **MINOR (HttpContextUserSessionProvider location)** — kept in `Makables.Config`. Moving it to `Infra.Common` would force that project to take a dependency on `Microsoft.AspNetCore.Http`, which leaks ASP.NET Core into a layer that should stay framework-agnostic. The class is small and lives next to the wiring that registers it.

## Acceptance criteria
- **AC-1** Build clean; 356 tests pass (274 unit + 82 integration).
- **AC-2** Every Web host enforces its audience policy end-to-end through the real `AddMakablesAuth` wiring against a real `JwtIssuer`-signed token.
- **AC-3** Cross-audience tokens (customer→maker, maker→customer) return 401.
- **AC-4** Admin host accepts only admin; rejects customer + maker tokens.
- **AC-5** Public host accepts any of customer/maker/admin (intentional contract — see B-1 resolution).
- **AC-6** Tokens signed by a different key / with a different issuer / expired / `alg=none` / malformed are rejected.
- **AC-7** A missing Authorization header returns 401.
- **AC-8** The deferred-config indirection works (integration tests' injected `Jwt:SigningKeyBase64` is honored) AND a missing / short / non-base64 key crashes the host at boot via `ValidateOnStart`.
- **AC-9** `MapInboundClaims = false` is pinned: `sub` and `role` claims survive verbatim to the protected endpoint.
- **AC-10** `HttpContextUserSessionProvider` fails closed if an authenticated principal lacks `sub`.

## Out of scope
- HTTP endpoints / controllers — T-0035.
- Per-role authorization policies beyond audience binding — handled per-endpoint in T-0035+. T-0035 MUST honor the Public-host contract (mount named policy when restricting).
- Refresh-token cookie wiring — T-0035.

## Status log
- 2026-05-24 initial commit 5aca948. 14 integration-test facts pass against all four Web hosts; full suite 351 green.
- 2026-05-24 reviewer fix folded in. Sec B-1 contract closed; sec M-1/M-2/M-3/M-4 closed; CQ M-1/M-2 closed. 5 new negative-path facts. Full suite 356 green.
