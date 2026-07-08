---
id: 0026
title: Apple Sign-In as a fourth OAuth identity provider — ES256 JWT client secret, form_post callback, first-auth-only profile delivery
status: accepted
date: 2026-07-07
deciders: [Architect]
living_docs: [docs/architecture/patterns.md]
---

# 0026 — Apple OAuth

## Context

ADR 0012 established email/password + magic link + Google OAuth, all converging on the same `User` record by email. T-0026/T-0035 shipped Google OAuth using a server-side authorization-code flow: `IOAuthStateSigner` mints an HMAC-signed, HKDF-domain-separated state binding `Audience/RedirectUri/CsrfCookieHash/Nonce/IssuedAt`; `CompleteGoogleOAuth` verifies it, exchanges the code, and resolves-or-creates a `User` via email-match.

T-0139 adds Apple Sign-In as a fourth provider (US-customer-0004, Apple-device conversion). Apple's OAuth mechanics diverge from Google's in three material ways that need a documented decision so implementation and review have a fixed reference point:

1. **No static client secret.** Apple requires a client secret minted as a **short-lived ES256-signed JWT**: `iss=TeamId`, `sub=ClientId` (the Services ID), `aud=https://appleid.apple.com`, `iat`/`exp` (≤ 6 months per Apple's docs; we mint on-demand with a 15-minute expiry — no caching, no rotation job), `kid=KeyId` in the JWT header, signed with Apple's issued P-256 private key (`.p8`, from Key Vault, never logged).
2. **`response_mode=form_post`.** Apple's web flow POSTs the callback (`code`, `state`, optional `user`) as a form body, not a GET query string. The callback controller/action must accept a POST body — a genuinely different HTTP contract from Google's GET callback.
3. **First-authorization-only profile delivery.** Apple sends the user's name/email in a separate `user` JSON form field **only on the first authorization** for a given (app, Apple ID) pair. Subsequent logins carry only the `id_token` (with `sub`, `email`, `email_verified`) — no name. `email_verified` is sometimes serialized as the **string** `"false"`/`"true"` rather than a JSON boolean — a documented Apple quirk that must be handled in profile parsing, not assumed away.

This ADR decides whether to amend ADR 0012 in place or author a new ADR, and records these three deltas as first-class, reviewable decisions rather than leaving them implicit in ticket prose.

## Decision

**Author a new ADR (this one, 0026) rather than amending ADR 0012.** ADR 0012 is `accepted` and per ADR-rule discipline is never edited in place — decisions are superseded, not mutated. Apple is not a superseding change to the *authentication model* (password/magic-link/OAuth convergence, JWT shape, refresh-token rotation, lockout, audience enforcement all stand unchanged) — it is an **additive** identity provider under the same model. A superseding ADR would incorrectly imply ADR 0012's core decisions changed. Instead, 0026 extends the "Identity providers at launch" list conceptually to "Identity providers" (Apple joins post-launch under the same architecture) and documents only what's provider-specific to Apple. ADR 0012 remains the source of truth for the shared machinery (`IOAuthStateSigner`, JWT structure, refresh tokens, lockout, account merging); 0026 cites it rather than repeating it.

**Applies to:** Backend only (`Core.Domain/Identity`, `Infra.Clients/Apple`, `Core.AppServices/Features/Auth`, `Web.*` controllers). Frontend changes (Apple button, `startAppleOAuth` wrapper, i18n keys) are pure presentation and follow the existing Google-button pattern with no new frontend architectural decision — no frontend ADR content required.

### Apple-specific mechanics (normative)

- **Client secret:** `AppleClientSecretSigner` (in `Infra.Clients/Apple/`, per §A.15/A.16 provider-adapter placement) mints an ES256 JWT per token-exchange call. No caching — mint fresh, use once, discard. This trades a few extra milliseconds of CPU for zero rotation/expiry-tracking complexity, which is the right trade for a secret used once per login.
- **Callback contract:** `StartAppleOAuth` builds the authorization URL with `response_mode=form_post` and `scope=name email`. The corresponding controller action for `/api/public/auth/apple/callback` is `[HttpPost]` and reads `code`/`state`/`user` from form fields (`[FromForm]`), not query string. This is the one place the Apple flow's HTTP shape differs from Google's controller action; the `CompleteAppleOAuth` command/handler contract is otherwise identical in shape to `CompleteGoogleOAuth`.
- **Profile parsing:** `AppleProfile` record parses `email_verified` accepting both native JSON boolean and the string-typed `"true"`/`"false"` form Apple is documented to sometimes emit. `AppleOAuthClient` captures the one-time `user` field's name when present; when absent (repeat login), falls back to `id_token` claims only. `CompleteAppleOAuth`'s `ResolveOrCreateUserAsync`-equivalent uses the `user`-field name only at `User.Create` time (new account); for the link-by-email and match-by-sub branches, name is never overwritten, mirroring Google's semantics where account creation is the only point name is set.
- **State signing:** reuses `IOAuthStateSigner` unchanged (see the Defense section below for why no `Provider` claim is added).
- **Account merging:** identical semantics to ADR 0012 §Account merging — email is the convergence key; first Apple login for an existing password/Google user links `AppleSub` and confirms email, exactly mirroring `GoogleSub` linking.
- **Admin audience:** Apple OAuth is rejected for `audience=admin` at both `StartAppleOAuth` and `CompleteAppleOAuth`, identically to Google (ADR 0012's admin-must-use-password rule is provider-agnostic, not Google-specific).

## Alternatives considered

- **Amend ADR 0012 in place** — rejected. ADR 0012 is `accepted`; the ADR-rule discipline (`docs/adr/0009-numbering.md` and the ADR process in this charter) forbids editing accepted ADRs. Amending would also conflate "the authentication model" (unchanged) with "one provider's wire-format quirks" (Apple-specific), making future reviews harder to scope.
- **Client-side "Sign in with Apple JS" SDK, backend verifies token only** — rejected (also rejected in the ticket's own alternatives analysis, which this ADR endorses): it would require a second, differently-shaped trust boundary (browser-held ID token vs server-held authorization code) alongside Google's server-side flow, doubling the security review surface for the OAuth seam with no material benefit — Apple's server-side authorization-code flow is fully supported and lets us reuse `IOAuthStateSigner`, admin-rejection, and account-merge-by-email verbatim.
- **A single generic `IOAuthProvider` abstraction spanning Google and Apple, collapsing `StartGoogleOAuth`/`StartAppleOAuth` into one generic handler** — rejected for this ADR's scope. The two providers' actual deltas (static vs JWT client secret, GET vs POST callback, always-present vs first-auth-only profile) are large enough that a forced-generic abstraction would need conditional branches inside a "shared" handler — worse than two parallel, structurally-identical-but-separate feature files per §A.2/A.7 (feature-folder, one-file-per-use-case). If a third provider arrives, revisit consolidation then; premature abstraction across two data points is a known anti-pattern.

## Consequences

- **Positive:** Apple joins the identity-provider set without touching ADR 0012's accepted content; `IOAuthStateSigner`, JWT issuance, refresh-token rotation, and account-merge-by-email are proven, already-reviewed primitives reused as-is. The blast radius of "Apple is different" stays isolated to `Infra.Clients/Apple/` and one controller action's HTTP verb/body-source.
- **Positive:** the `email_verified` string-vs-bool quirk and first-auth-only `user` field are documented here once, preventing future implementers from rediscovering them via a production incident.
- **Negative:** a fourth `Infra.Clients/<Provider>/` surface adds ~similar LOC to Google's wrapper (client, options, exception type) — mitigated by mirroring Google's file shape exactly, minimizing review cost.
- **Negative:** Apple private key (`.p8`) management is a new Key Vault secret class requiring the same rotation playbook as other auth secrets (T-0134 runbook) — no forced expiry from Apple, so this depends on operational discipline rather than a hard technical deadline.
- **Neutral:** no change to JWT structure, audience enforcement, refresh-token shape, or lockout — those remain exactly as ADR 0012 specifies.

## Compliance / verification

- SecOps/reviewer: confirm `AppleClientSecretSigner` mints a fresh, unshared, unlogged JWT per exchange (`kid` header present, `exp` ≤ Apple's max, private key never appears in logs or exceptions).
- Reviewer: confirm the Apple callback controller action is `[HttpPost]` reading `[FromForm]` parameters, not `[FromQuery]`.
- Reviewer: a passing test asserts `email_verified` parses correctly for both the string `"false"` and boolean `false` forms (AC-4 in T-0139).
- Reviewer: a passing test asserts `FullName` is populated from the one-time `user` field on first authorization and left correctly unset/unsourced on repeat logins where the field is absent (AC-6/AC-7).
- Reviewer: admin-audience rejection test exists for both `StartAppleOAuth` and `CompleteAppleOAuth` (AC-2), mirroring the existing Google test.
- Reviewer: confirm no `Provider` claim was silently omitted from `IOAuthStateSigner`'s payload without the Defense-section reasoning below being re-verified against the actual `RedirectUri` values configured for both providers (they must remain distinct routes).

## Defense

- **Challenge (self-raised, per T-0139 Technical notes):** does `IOAuthStateSigner`'s payload (`Audience/RedirectUri/CsrfCookieHash/Nonce/IssuedAt` — no `Provider` field) allow a state minted by `StartGoogleOAuth` to be replayed against `CompleteAppleOAuth`, since both could in principle share the same audience and (if misconfigured) the same redirect URI?
  - **Response: rebut — not exploitable given the current and required routing, for two independent reasons; see the architect's full analysis in `docs/tickets/T-0139-apple-oauth-login.md` Technical notes and `docs/adr/0026-apple-oauth.md`'s own text above.** (1) `OAuthStateSigner.TryVerify` (`backend/src/Makables.Infra.Common/Auth/OAuthStateSigner.cs:142`) performs an **exact ordinal compare** of the state's bound `RedirectUri` against the `RedirectUri` presented at the callback. Google's callback route is `/api/public/auth/google/callback`; Apple's is `/api/public/auth/apple/callback` (T-0139 scope, distinct routes by construction) — so their redirect URIs are structurally distinct strings, and a Google-minted state cannot pass `CompleteAppleOAuth`'s redirect-URI check unless an implementer mistakenly configures both providers with the *same* redirect URI, which is a configuration error, not a design gap. (2) Even in that misconfiguration scenario, the attacker would still need to supply a `code` — and `code` is provider-specific: Google's authorization code is only redeemable at Google's token endpoint; Apple's is only redeemable at Apple's (`https://appleid.apple.com/auth/token`). `CompleteAppleOAuth`'s handler calls `IAppleOAuthClient.ExchangeCodeAsync`, which talks only to Apple's token endpoint — a Google-issued code presented there is rejected by Apple with `invalid_grant` before any user-resolution logic runs. The `code` is the true single-use, provider-bound secret in the authorization-code flow; the state token's job is CSRF/redirect binding, not provider discrimination.
  - **Conclusion:** no code change required before Apple ships. **Recommendation (non-blocking, defense-in-depth polish):** a future ADR/ticket may add an explicit `Provider` claim to `OAuthStatePayload` purely for auditability and to remove reliance on "redirect URIs happen to differ" as an implicit invariant — but this is optional hygiene, not a security fix, and must not block T-0139's `ready` status. If pursued, track as a fast-follow ticket, not a T-0139 blocker.

## Related
- ADR: 0012 — Authentication (shared model: JWT, refresh tokens, lockout, account merging, `IOAuthStateSigner`)
- ADR: 0009 — Numbering
- Ticket: T-0139 — Apple Sign-In
- Ticket: T-0026 — Google OAuth (the mirrored precedent)
- Patterns: §A.17 Authentication (custom) — updated in this same pass to reflect Google shipped + Apple planned
- User story: US-customer-0004
