---
id: T-0028
title: Email pipeline — SendGrid Dynamic Templates + DB-backed translation + ILanguageResolver + outbox payload carries LanguageCode
status: done
size: L
owner: dotnet-backend
created: 2026-05-24
updated: 2026-05-24
depends_on: [T-0011, T-0020, T-0023, T-0024, T-0025]
blocks: [T-0029, T-0035]
adrs: [0019]
phase: 2
---

# T-0028 — Email pipeline (SendGrid)

## Scope

End-to-end transactional-email pipeline for Phase-2 auth flows. Closes the email half of the magic-link / email-confirmation / password-reset triplet (T-0023 → T-0025) so T-0029 can implement the outbox-drain Function as a thin scheduler.

ADR 0019 amended in the same commit — see §"Amendment 2026-05-24". Original Resend + MJML decision reversed in favour of SendGrid Dynamic Templates + DB-backed translation per `(EmailTemplateType, LanguageCode)`, per user directive at sprint planning.

### Domain (`Core.Domain/`)
- `Common/LanguageCode.cs` — BCP-47 constants (`CsCZ`, `EnUS`), `Supported` array, `DefaultFallback` (`"cs-CZ"` — Czech launch market), strict `IsValid(string?)` validator.
- `Common/BusinessErrorMessage.cs` — **moved** from `Core.AppServices.Common` so adapters in `Infra.Clients` can reference it (Clean Architecture: `Infra.*` can reference `Core.Domain` but not `Core.AppServices`). 25 importers updated to import the new namespace. New codes added: `EmailTemplateNotFound`, `EmailTemplateTranslationMissing`, `EmailProviderTransientFailure`, `EmailProviderPermanentFailure`, `EmailPayloadInvalid`, `EmailEventTypeUnknown`.
- `Email/EmailTemplateType.cs` — enum (`AuthMagicLink`, `AuthEmailConfirmation`, `AuthPasswordReset`).
- `Email/EmailTemplate.cs` — per-type master record. Carries `ProviderTemplateId` (SendGrid `d-...` id), optional `FromAddress` / `FromName` / `ReplyToAddress` overrides.
- `Email/EmailTemplateTranslation.cs` — per-`(template_id, language)` row carrying subject + plain-text body (multipart alternate + admin preview).
- `Email/IEmailProvider.cs` + `EmailMessage` + `EmailSentReceipt` — adapter contract per ADR 0019 (amended).
- `Email/IEmailTemplateRepository.cs` + `Email/IEmailTemplateTranslationRepository.cs`.
- `Identity/User.cs` — new `PreferredLanguage` (nullable BCP-47) + `SetPreferredLanguage(string? bcp47)` mutator that validates via `LanguageCode.IsValid`.
- `Outbox/OneTimeTokenOutboxPayload.cs` — added `LanguageCode` field (resolved at enqueue time so the email is sent in the language the user had when they triggered the flow).

### Core.AppServices
- `Common/ILanguageResolver.cs` + `LanguageResolver` — `User.PreferredLanguage → CountryConfiguration.DefaultLanguageCode → LanguageCode.DefaultFallback`.
- `Common/PublicAppUrlsOptions.cs` — `PublicAppUrls:WebBaseUrl` + per-template path templates with `{token}` placeholder.
- `Features/Email/IEmailSendService.cs` + `EmailSendService` — orchestrator T-0029 calls. Decodes payload → looks up template → resolves translation with one-step fallback to `LanguageCode.DefaultFallback` → builds `action_url` from `PublicAppUrlsOptions` (URL-escaped token) → substitutes `{{...}}` placeholders into plain-text body → dispatches via `IEmailProvider`.
- `Features/Auth/OneTimeTokenIssuer.cs` — gained `ILanguageResolver` dependency. Resolves language unconditionally (sentinel `User` on the no-user branch) so the timing-equalization invariant T-0023 B-1 is preserved — same DB-roundtrip count on both branches.

### Infra.Clients/SendGrid
- `SendGridOptions.cs` — `SendGrid:ApiKey` (Key Vault ref in prod), `DefaultFromAddress`, `DefaultFromName`, `RetryCount`, `RetryBaseDelayMs`.
- `SendGridEmailProvider.cs` — implements `IEmailProvider`. Uses `MailHelper.CreateSingleTemplateEmail` for dynamic-template + data dictionary; ships `PlainTextBody` as multipart/alternative; extracts `X-Message-Id` as the receipt id; classifies upstream errors as Transient (408 / 429 / 5xx) vs Permanent.

### Infra.Database
- `Configurations/UserConfiguration.cs` — `preferred_language` column (`VARCHAR(16)`, nullable).
- `Configurations/EmailTemplateConfiguration.cs` — partial unique index on `type` (active rows).
- `Configurations/EmailTemplateTranslationConfiguration.cs` — partial unique index on `(email_template_id, language_code)` (active rows).
- `Repositories/EmailTemplateRepository.cs` + `EmailTemplateTranslationRepository.cs` — `.AsNoTracking()` lookups.
- `Migrations/20260524190759_EmailTemplates.cs` — schema + seed:
  - Adds `users.preferred_language`.
  - Creates `email_templates` + `email_template_translations` tables.
  - Flips CZ row `default_email_provider` `"resend"` → `"sendgrid"`.
  - Inserts 3 `EmailTemplate` rows (with placeholder `d-...` provider template ids — admin pastes real ids before launch) + 6 `EmailTemplateTranslation` rows (3 templates × cs-CZ + en-US) with production-quality copy.
- `Seeding/CountrySeed.cs` — `defaultEmailProvider: "sendgrid"` (was `"resend"`).

### DI
- `AddMakablesClients`: `SendGridOptions`, `ISendGridClient` (singleton), `ResiliencePipeline<Response>` (Polly v8 retry on transient codes + exception types), `IEmailProvider = SendGridEmailProvider` (singleton).
- `AddMakablesInfrastructure`: `IEmailTemplateRepository`, `IEmailTemplateTranslationRepository`, `ILanguageResolver`, `PublicAppUrlsOptions`, `IEmailSendService`.

### Packages
- `SendGrid 9.29.3`.
- `Polly 8.4.2`.

### Tests (+54 facts; 410 total = 328 unit + 82 integration)
- `Domain/Common/LanguageCodeTests.cs` — accept/reject matrix + Supported + DefaultFallback.
- `Domain/Identity/UserTests.cs` — added `SetPreferredLanguage` accept/reject + clear-via-null facts.
- `Domain/Email/EmailTemplateTests.cs` + `EmailTemplateTranslationTests.cs` — factories normalize + reject bad inputs.
- `AppServices/Common/LanguageResolverTests.cs` — user > country > fallback chain pinned.
- `AppServices/Features/Email/EmailSendServiceTests.cs` — happy path, URL-escaping of token, one-step fallback to platform default, translationMissing, templateNotFound, eventTypeUnknown, malformed JSON, payload missing fields, provider-failure bubble-back (9 facts).
- `Infra/Clients/SendGrid/SendGridEmailProviderTests.cs` — Code constant, X-Message-Id extraction, empty-From → SendGridOptions default, transient/permanent classification matrix, cancellation propagation (8 facts).
- Existing `OneTimeTokenIssuerTests` updated for the new `ILanguageResolver` ctor dep; all five facts still hold (timing invariant preserved).

## ADR amendment

`docs/adr/0019-email-resend.md` — front-matter `status` flipped `accepted` → `amended`; new top section §"Amendment 2026-05-24 (T-0028)" documents the Resend→SendGrid pivot, the DB-backed translation choice, what stayed the same, and exactly which sub-sections of the original ADR are now wrong vs still hold. The original body is preserved below for context.

## Reviewer findings and resolutions (commit ed891ed)

Two reviewers ran in parallel.

### Security reviewer — BLOCKER × 2 + MAJOR × 3

- **B-1 SendGrid response body propagated through Error.Details + logged structured** — SendGrid 4xx responses can echo recipient PII and (rarely) request headers. The body was reaching `Error.Permanent/Transient(..., body)` and a structured log property `Body` that the `SensitivePropertyMasker` did NOT cover (`"body"` isn't on its pattern list). **Fixed:** `SendGridEmailProvider` no longer propagates the body. Failures return `Error.*(code)` with no details. The body is logged only at Debug level, truncated to 512 chars, under a property name (`TokenBody`) that the masker's `"token"` pattern redacts. Status code goes out as a separate Warning. Pinned by `Failure_responses_never_carry_the_SendGrid_response_body_in_the_returned_Error`.
- **B-2 `WebBaseUrl` unvalidated** — `javascript:`/`data:`/hostile-host would produce phishing-grade clickable links in every transactional email. **Fixed:** new `PublicAppUrlsOptionsValidator` enforces (a) `WebBaseUrl` is absolute https (or http on loopback for dev), (b) every path template starts with `/`, (c) every path template contains the literal `{token}` placeholder. Wired with `.Validate(...).ValidateOnStart()` in `AddMakablesInfrastructure`. Misconfig now crashes the host at boot. Pinned by `PublicAppUrlsOptionsValidatorTests` (7 facts).
- **M-1 Silent missing-`{token}` substitution** — Closed by the B-2 validator (every path template MUST contain the placeholder). `BuildActionUrl` comment notes the invariant.
- **M-3 No `ValidateOnStart` on `SendGridOptions`** — **Fixed:** `services.AddOptions<SendGridOptions>().Validate(...).ValidateOnStart()` checks `ApiKey` + `DefaultFromAddress` non-empty + `RetryCount` 0..10 + `PerSendTimeoutSeconds` 1..60 at boot.
- **M-4 Retry budget compounds with outbox-level retry** — **Fixed:** default `SendGridOptions.RetryCount` reduced 3 → 1 (outbox owns the authoritative retry budget per ADR 0019); added `SendGridOptions.PerSendTimeoutSeconds` (default 10) which `SendGridEmailProvider` enforces via a linked `CancellationTokenSource.CancelAfter` so a stuck connection can't pin an outbox-processor worker. Per-call timeout fires as `Transient` so outbox-level retry takes over.
- **MIN-1/3/5 (deploy-checklist items)** — accepted; not in code. To be tracked in a follow-up `docs/security/email-deliverability.md` (Outlook Safe-Links posture, SPF/DKIM/DMARC verification before flipping to sendgrid, secret-rotation runbook).
- **MIN-4 / N-2 (`LanguageCode` as untrusted input + probe sentinel timing invariant)** — verified clean; no action needed.

### Code-quality reviewer — 0 BLOCKERs + 2 MAJORs + N MINORs

- **M-1 `EmailMessage.Subject` silently dropped by `SendGridEmailProvider`** — **Fixed:** provider now calls `sgMessage.SetSubject(message.Subject)` AND injects `subject` into the dynamic-template data dictionary so the SendGrid template can render it in the HTML body too. Pinned by `Subject_is_forwarded_to_SendGrid_message_AND_data_dictionary`.
- **M-2 Dead `using Makables.Core.AppServices.Common;` in 12 auth handlers + 8 auth tests** — **Fixed:** stripped from 20 files. Encoding preserved (UTF-8 with original BOM-or-not state retained per file).
- **N-3 `UnknownUserProbe` sentinel User aggregate is fragile coupling** — **Fixed (cleaner alternative):** added `ILanguageResolver.ResolveAsync(string? preferredLanguage, string countryCode, ct)` overload. `OneTimeTokenIssuer` calls it with `(user?.PreferredLanguage, user?.CountryCodePrimary ?? "CZ")`. No sentinel `User`, no `TypeInitializationException` risk, no `User` aggregate misuse. Pinned by `ResolveAsync_*` facts in `LanguageResolverTests`.
- **N-4 `EmailPayloadInvalid` overloaded** — **Fixed:** split into `EmailPayloadMalformed` (JSON decode crashed) and `EmailPayloadMissingFields` (decoded but blank). T-0029's triage UI can distinguish producer-side malformed-payload from missing-field bugs.
- **N-2 `LanguageCode.IsValid` strictness vs column width** — **Documented in xmldoc:** validator is intentionally strict (script + variant + 3-letter rejected at launch); 16-char column is forward-compatible for when those land.
- **N-5 `SubstitutePlainTextPlaceholders` HTML-XSS footgun if reused** — **Fixed:** added `SECURITY:` comment forbidding HTML reuse.
- **T-1 Step-number comments duplicate xmldoc** — **Fixed:** dropped numbered-step comments in `EmailSendService`.
- **T-2 `ExtractMessageId` micro-style** — **Fixed:** now `values.FirstOrDefault() ?? string.Empty`.
- **N-1 / N-7 / N-8 / N-9 / N-10 / N-6 / T-3 / T-4** — accepted as-is or already correct.

### Test deltas (+21 facts; 431 total = 349 unit + 82 integration)
- `PublicAppUrlsOptionsValidatorTests` — 7 facts (accept defaults, reject `javascript:` / `data:` / `ftp:` / non-loopback `http`, reject malformed, accept loopback http + https, reject path without `{token}`, reject path without leading `/`, reject blank path).
- `LanguageResolverTests` — 5 new `ResolveAsync(...)` overload facts (explicit preferred wins; null falls to country; malformed falls to country; unknown country falls to platform default; lookup runs unconditionally so timing-invariant proxy holds).
- `SendGridEmailProviderTests` — 2 new facts (subject forwarded to wire; failure body never propagated into Error).
- `EmailSendServiceTests` — split former `payloadInvalid` test into `Returns_payloadMalformed_for_malformed_json` + `Returns_payloadMissingFields_for_payload_decoded_but_blank`.

### Integration test infrastructure
- `JwtAuthMiddlewareTests` + `WebHostStartupTests` now seed `SendGrid:ApiKey` + `SendGrid:DefaultFromAddress` + `PublicAppUrls:WebBaseUrl` because both options classes are now `ValidateOnStart`. Stub values; tests never actually call SendGrid.

## Acceptance criteria
- **AC-1** Build clean; 431 tests pass (349 unit + 82 integration).
- **AC-2** `OneTimeTokenOutboxPayload` carries `LanguageCode`; `OneTimeTokenIssuer` resolves it via `ILanguageResolver` on every branch (T-0023 B-1 timing invariant preserved).
- **AC-3** `ILanguageResolver` honours the order: `User.PreferredLanguage → CountryConfiguration.DefaultLanguageCode → LanguageCode.DefaultFallback`.
- **AC-4** `EmailSendService` composes `EmailMessage` from outbox event-type + payload + DB template/translation; URL-escapes the token; substitutes `{{action_url}}` / `{{expires_at}}` into the plain-text body.
- **AC-5** `EmailSendService` falls back from the requested language to `LanguageCode.DefaultFallback` if the exact translation row is missing.
- **AC-6** `EmailSendService` returns `Permanent` `BusinessErrorMessage.EmailTemplateNotFound` / `EmailTemplateTranslationMissing` / `EmailEventTypeUnknown` / `EmailPayloadInvalid` on the corresponding failure modes.
- **AC-7** `SendGridEmailProvider` returns a receipt with the `X-Message-Id` header value on 2xx; surfaces 408 / 429 / 5xx as `Transient` and 4xx as `Permanent`.
- **AC-8** Migration `20260524190759_EmailTemplates` adds the two tables, the `users.preferred_language` column, the CZ-row email-provider flip, 3 template rows, and 6 translation rows.
- **AC-9** ADR 0019 amended (front-matter + amendment section + preserved original body).
- **AC-10** CLAUDE.md rules honoured: `Core.Domain` has no third-party packages; `Infra.Clients` does not reference `Core.AppServices`; no `SaveChangesAsync` outside the pipeline; all error codes come from `BusinessErrorMessage`; money / RLS / per-country rules untouched (no order of magnitude change in this ticket).

## Out of scope
- Bounce / event-webhook handler — follow-up ticket (deferred per user directive; add an `email_event` table + `/api/public/webhooks/sendgrid` with signature verification).
- Filling in real SendGrid `d-...` template ids — admin UI work, parallel ticket; placeholder ids are in the seed for now.
- `ProcessOutboxFunction` itself — T-0029.
- Frontend `/auth/*` pages that consume the action URLs — T-0035.
- Order / payout / message email templates — Phase 4/5+ tickets, additional `EmailTemplateType` values.

## Status log
- 2026-05-24 initial commit ed891ed. 410 tests pass. ADR 0019 amended.
- 2026-05-25 reviewer fix folded in. Sec B-1 / B-2 closed; sec M-1/M-3/M-4 closed; CQ M-1/M-2 closed; CQ N-3/N-4/N-5/T-1/T-2 closed. 431 tests pass (349 unit + 82 integration; +21 facts).
