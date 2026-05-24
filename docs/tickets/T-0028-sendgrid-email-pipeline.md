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

## Acceptance criteria
- **AC-1** Build clean; 410 tests pass (328 unit + 82 integration).
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
- 2026-05-24 done. 410 tests pass. ADR 0019 amended. Awaiting dual reviewer (security + code-quality) per workflow.
