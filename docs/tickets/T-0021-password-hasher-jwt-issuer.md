---
id: T-0021
title: IPasswordHasher (Argon2id) + IJwtIssuer (HS256+audience) + tests
status: done
size: M
owner: dotnet-backend
created: 2026-05-24
updated: 2026-05-24
depends_on: [T-0020]
blocks: [T-0022]
adrs: [0012]
phase: 2
---

# T-0021 — Password hasher + JWT issuer

## Scope

### Core.Domain (`Identity/`)
- `IPasswordHasher.cs` — `Hash` / `Verify` / `NeedsRehash`. Versioned output so a parameter bump doesn't break existing hashes.
- `IJwtIssuer.cs` — `Issue(User, audience, now)` → `AccessToken(Token, ExpiresAt, Jti)`.

### Infra.Common (`Auth/`)
- `Argon2idOptions.cs` — `Auth:Argon2id` config section. Defaults: 64 MiB / 3 iter / 1 lane / 16 B salt / 32 B hash per ADR 0012 §Password policy.
- `Argon2idPasswordHasher.cs` — Konscious-backed implementation. Storage format `argon2id$v=19$m=<m>,t=<t>,p=<p>$<saltB64>$<hashB64>`. Constant-time compare via `CryptographicOperations.FixedTimeEquals`.
- `JwtOptions.cs` — `Jwt` config section (Issuer + SigningKeyBase64 + AccessTokenLifetime + KeyId).
- `JwtIssuer.cs` — `JsonWebTokenHandler`-based HS256 issuer. Validates the signing key is ≥ 32 bytes after base64 decoding at ctor time so misconfiguration fails loudly during DI resolution. Mints claims: `sub`, `email`, `role`, `country_code`, `aud`, `iss`, `iat`, `nbf`, `exp`, `jti` (ULID), plus `ClaimTypes.NameIdentifier` mirrored to `sub` for ASP.NET claim-helper compatibility.

### Config
- `AddMakablesInfrastructure.cs` — binds both option sections and registers `IPasswordHasher` + `IJwtIssuer` as singletons.

### Tests
- `Makables.Tests/Infra/Auth/Argon2idPasswordHasherTests.cs` — 9 facts: hash/verify round-trip, wrong-password rejection, salt randomness, prefix shape, malformed-hash safety, empty-input safety, NeedsRehash on bumped params + fresh hash + malformed input.
- `Makables.Tests/Infra/Auth/JwtIssuerTests.cs` — 15 facts: token shape per audience, audience rejection (3 invalid cases), all ADR claims present, validation with same key, audience-mismatch rejection (the "customer token replayed at maker host" case), key-mismatch rejection, ctor guard rejections (missing key / short key / missing issuer / zero lifetime), per-call jti uniqueness.
- `Makables.IntegrationTests/HostStartup/WebHostStartupTests.cs` — adds deterministic `Jwt:Issuer` + `Jwt:SigningKeyBase64` (32 zero bytes) to the test config so JwtIssuer's eager validation can construct the singleton if a test asks for it.

### Packages
- `Konscious.Security.Cryptography.Argon2 1.3.1`
- `Microsoft.IdentityModel.JsonWebTokens 8.18.0`
- `Microsoft.IdentityModel.Tokens 8.18.0`
- `Microsoft.Extensions.Options 10.0.0` + `…Options.ConfigurationExtensions` (so `.Bind` is available in Infra.Common).

## Out of scope
- Key rotation via `kid` map (ADR 0012 §JWT defers to a follow-up ADR; T-0021 ships a single-key issuer with `kid` claim already present so consumers don't have to break later).
- Authentication middleware on the four Web hosts — T-0027. T-0021 hands the validation parameters (issuer / key / audience) to T-0027 via the same `JwtOptions` section.
- Refresh-token issuance / SHA-256 hashing — lives in T-0022's `AuthService` because it interleaves with `IRefreshTokenRepository` writes.

## Acceptance criteria
- **AC-1** Build clean; 200 tests pass (159 unit + 41 integration; +24 new).
- **AC-2** `Hash` output starts with `argon2id$v=19$m=<m>,t=<t>,p=<p>$…` and is verifiable against the same plaintext.
- **AC-3** `Verify` is constant-time (uses `CryptographicOperations.FixedTimeEquals`); returns `false` on any malformed input.
- **AC-4** `NeedsRehash` returns true when stored parameters diverge from current policy and false otherwise; transparent re-hash on login is now a one-line check.
- **AC-5** `IJwtIssuer.Issue` produces a JWT that validates against the same signing key + issuer + audience, and is rejected when audience or signing key differ.
- **AC-6** `JwtIssuer` ctor refuses missing/short signing keys, missing issuer, non-positive token lifetime.

## Status log
- 2026-05-24 done. 200 tests pass. Q-0004 answered (login_attempt_buckets table); T-0022 unblocked.
