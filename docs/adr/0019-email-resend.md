---
id: 0019
title: Email — Resend transport + DB-backed translation (was SendGrid Dynamic Templates; originally Resend + MJML)
status: amended
date: 2026-05-21
amended: 2026-07-23
deciders: [Architect, user]
---

# 0019 — Email

## Amendment 2026-07-23 (T-0157) — provider back to Resend

Operator directive ("Change email service to RESEND") — and the confirmed
processor list from the 2026-07-04 business meeting (Q17) names **Resend**,
which the GDPR page already advertises. The provider swap rides the
T-0124 keyed-services-with-factory seam:

1. **Active provider: SendGrid → Resend.** New `ResendEmailProvider`
   (`Infra.Clients/Resend/`, keyed `"resend"`); CZ
   `CountryConfiguration.DefaultEmailProvider` reseeded to `"resend"`
   (data migration `SwitchDefaultEmailProviderToResend`). SendGrid stays
   registered as an inactive keyed adapter (Comgate precedent) — flipping
   back is a seed change, not a code change.
2. **Rendering moves fully local.** SendGrid's hosted dynamic-template
   HTML is gone; Resend receives the locally substituted
   `Subject` + `PlainTextBody` (`SubstitutePlainTextPlaceholders`) as a
   plain-text email. The DB model (`EmailTemplate` +
   `EmailTemplateTranslation`, subject + plain-text per language) is
   unchanged — it never stored HTML. A local HTML layout (single branded
   wrapper) is a candidate follow-up ticket, not part of the swap.
3. **`EmailMessage` contract unchanged** — `ProviderTemplateId`/`Data`
   are simply ignored by the Resend adapter, so every `EmailSendService`
   branch and seed row keeps working and a future provider can use them
   again.
4. **Secrets:** `Resend--ApiKey` joins the Key Vault inventory + both
   deploy workflows (dev boot-stub `re_dev_boot_stub`; prod fail-closed).
   Sender domain (`makables.cz`) must be verified in Resend before real
   mail flows; dev can use `onboarding@resend.dev` as
   `Resend:DefaultFromAddress` until then.

The 2026-05-24 SendGrid amendment below is preserved for context; its
DB-translation half remains in force — only the transport + remote-HTML
half is reversed.

## Amendment 2026-05-24 (T-0028)

The original decision (Resend + MJML templates compiled at build time, preserved below for context) is **reversed** in favour of **SendGrid Dynamic Templates + DB-backed translation per `(template_type, language)`**.

### What changed and why

1. **Provider: Resend → SendGrid.** The user directive at T-0028 sprint planning explicitly chose SendGrid Dynamic Templates after reviewing the cleansia codebase (which already runs this pattern in production). Reasons:
   - Templates live in SendGrid's editor — non-engineers can edit copy without a deploy.
   - The Dynamic Templates engine handles HTML / dark-mode / client compatibility — we don't ship MJML toolchain to CI.
   - Bounce / event-webhook semantics are a solved problem in SendGrid.
   - Cleansia has already de-risked the failure modes (Polly retry shape, X-Message-Id extraction, multipart plain-text alternate).
2. **Templates: MJML files on disk → DB rows (`EmailTemplate` + `EmailTemplateTranslation`).** One `EmailTemplate` row per `EmailTemplateType`; one `EmailTemplateTranslation` row per `(template_id, language_code)`. The translation carries subject + plain-text body (multipart alternate); the HTML is rendered by SendGrid from the dynamic-template + data dictionary. This is the cleansia pattern.
3. **Language resolution.** Resolved at outbox-enqueue time (not at consume) inside `OneTimeTokenIssuer` via `ILanguageResolver`: `User.PreferredLanguage → CountryConfiguration.DefaultLanguageCode → LanguageCode.DefaultFallback ("cs-CZ")`. The resolved value rides on the outbox payload (`OneTimeTokenOutboxPayload.LanguageCode`). T-0029's processor doesn't re-resolve — the language the user had when they triggered the email is the language they receive.
4. **Multi-language at launch.** cs-CZ and en-US are seeded from day one for the three Phase-2 templates (magic-link, email-confirmation, password-reset). Adding a new language is a row insert per template, not a new file in the repo.
5. **CountryConfiguration.** CZ seed `default_email_provider` flipped `"resend"` → `"sendgrid"` in migration `20260524190759_EmailTemplates`.
6. **Webhook for bounces.** Deferred. A follow-up ticket adds `/api/public/webhooks/sendgrid` with signature verification + `email_event` table. T-0028 ships the send pipeline only.

### What stayed the same

- **Outbox-mediated sends.** No handler ever calls `IEmailProvider.SendAsync` directly. The outbox is still the only chokepoint; T-0029's `ProcessOutboxFunction` is still the only consumer.
- **`IEmailProvider` / `EmailMessage` / `EmailSentReceipt` interface.** The shape from the original ADR is honoured, with one addition: `EmailMessage` now carries `ProviderTemplateId` + `Subject` + `PlainTextBody` + `Data` (the substitution dictionary) so the adapter has everything for a single SendGrid call.
- **No unsubscribe surface at MVP.** Transactional only. The original §"Unsubscribe / preferences" still holds.

### New shape: `IEmailProvider`

```csharp
public interface IEmailProvider
{
    string Code { get; }   // "sendgrid"
    Task<BusinessResult<EmailSentReceipt>> SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed record EmailMessage(
    string ProviderTemplateId,             // SendGrid d-... id from EmailTemplate
    string LanguageCode,                   // BCP-47, e.g. "cs-CZ"
    string ToAddress, string? ToName,
    string FromAddress, string? FromName,  // empty → SendGridOptions.DefaultFromAddress
    string? ReplyToAddress,
    string Subject,
    string PlainTextBody,
    IReadOnlyDictionary<string, object> Data);   // dynamic-template substitutions
```

### Implementation pointers

- Adapter: `Makables.Infra.Clients/SendGrid/SendGridEmailProvider.cs` (Polly v8 retry on 408 / 429 / 5xx; constructed `SendGridClient` singleton; X-Message-Id extracted from response headers as the receipt id).
- Composer: `Makables.Core.AppServices/Features/Email/IEmailSendService.cs` (decodes payload → loads template → loads translation with one-step fallback to `LanguageCode.DefaultFallback` → builds the `action_url` from `PublicAppUrlsOptions` → substitutes plain-text placeholders → dispatches).
- Resolver: `Makables.Core.AppServices/Common/ILanguageResolver.cs`.
- Entities: `Makables.Core.Domain/Email/EmailTemplate.cs`, `EmailTemplateTranslation.cs`, `EmailTemplateType.cs`, `IEmailProvider.cs`, `IEmailTemplateRepository.cs`, `IEmailTemplateTranslationRepository.cs`.
- Language tag validation + constants: `Makables.Core.Domain/Common/LanguageCode.cs`.

### What the original decision below is now wrong about

- §"Where templates live" (templates/<code>/<locale>/ folder structure with MJML) — **replaced** by the two DB tables.
- §"Template rendering" (Handlebars.Net substitution at runtime against MJML-compiled HTML) — **replaced** by SendGrid's dynamic-template engine. The plain-text alternate is the only place we still do server-side substitution, and that's a tiny `{{key}}` replace in `EmailSendService`.
- §"Resend adapter" — **replaced** by `SendGridEmailProvider`.
- §"Configuration" (`Resend:ApiKey` etc.) — **replaced** by `SendGrid:ApiKey` / `SendGrid:DefaultFromAddress` / `SendGrid:DefaultFromName` / `SendGrid:RetryCount` / `SendGrid:RetryBaseDelayMs`.
- §"Bounces / complaints" — deferred (see point 6 above).

The rest of the original ADR — outbox mediation, locale resolution rule, multi-country posture, no-marketing-at-launch — still holds.

---

# 0019 — Email (Resend) [ORIGINAL, PRESERVED]

## Context

Per `TISKNI_MVP_SPEC.md` we send ~10 transactional emails: welcome, new-order, order-paid, order-accepted, order-shipped, order-delivered, auto-delivered, payout-sent, new-message, review-received. We chose Resend in the spec. We need to decide where templates live, how they're versioned, how locale variation is handled, and how sending interacts with the outbox.

## Decision

### Role: EmailProvider

`docs/architecture/roles/email-provider.md` (adapter role):

**Responsibility:** Render a named template with given data for a recipient locale, and submit the message to the upstream mail service.

**Collaborators:**
- `EmailTemplate` (read: subject + body for given template + locale)
- (None from `Order`/`Maker`/etc. — the *caller* assembles the data)

**Does NOT know:**
- Why the email is being sent
- Whether the recipient has unsubscribed (unsubscribe handling lives at the outbox / consent layer)
- Localization rules (the caller passes the resolved locale)

### Interface

```csharp
public interface IEmailProvider
{
    string Code { get; }   // "resend", "sendgrid", ...

    Task<BusinessResult<EmailSentReceipt>> SendAsync(
        EmailMessage message,
        CancellationToken ct);
}

public record EmailMessage(
    string TemplateCode,         // "order-paid"
    string Locale,               // "cs-CZ"
    string ToAddress,
    string? ToName,
    IReadOnlyDictionary<string, object> Data,    // template variables
    string? ReplyToAddress = null
);

public record EmailSentReceipt(string ProviderMessageId, DateTimeOffset SentAt);
```

### Where templates live

`backend/src/Makables.Infra.Clients/Resend/Templates/<TemplateCode>/<Locale>/`:

```
templates/order-paid/cs-CZ/
├── subject.txt
├── body.mjml             # MJML source compiled to HTML at build time
├── body.txt              # plaintext fallback
└── meta.json             # { from, replyTo, attachments policy, etc. }
```

**MJML** is the chosen authoring format. Reasons:
- Renders consistently across email clients (Outlook, Gmail, Apple Mail) — solved problem.
- Compiles to HTML at build time; runtime cost is zero.
- Plain text and source diffable, unlike binary export from a WYSIWYG.

A custom build step (`make-templates` MSBuild task) compiles `body.mjml` → `body.html` during `dotnet build`. The compiled HTML is committed alongside the MJML source so the build is reproducible even without MJML toolchain installed.

**Versioning:** templates are not separately versioned. Git history is the version log. If we ever need A/B testing or scheduled template rollouts, we revisit.

### Template rendering

`Makables.Infra.Common.Templating.HandlebarsTemplateRenderer` (using `Handlebars.Net`) substitutes `{{variable}}` in subject and body. Variables come from `EmailMessage.Data`. Renderer is locale-agnostic; the **template selection** is locale-aware (`templates/order-paid/cs-CZ/`).

If a template doesn't exist for the requested locale, the renderer falls back to the country's default language (from `CountryConfiguration.DefaultLanguageCode`), and ultimately to `en-US` if that's missing too. Missing templates raise a `Configuration` error rather than sending malformed mail.

### Resend adapter

Lives in `Makables.Infra.Clients/Resend/ResendEmailProvider.cs`.

POST to Resend's REST API with the rendered subject, HTML body, plaintext body, `from`, `to`, and any attachments (e.g. invoice PDFs).

**Configuration:**
- `Resend:ApiKey`
- `Resend:DefaultFromAddress` (e.g. `objednavky@makables.cz`)
- `Resend:DefaultFromName` (e.g. `Makables`)

The `from` for a given template can be overridden in its `meta.json` (e.g. payout emails come from `vyplaty@makables.cz`).

### Integration with the outbox

Email sends are **always** mediated by the outbox (ADR 0016). The caller never invokes `IEmailProvider.SendAsync` directly. The flow:

1. A handler decides "this event should trigger an email". It inserts an outbox row of type `email.send` with payload `{ templateCode, locale, toAddress, data }`.
2. The `ProcessOutbox` Function picks it up and calls `IEmailProvider.SendAsync`.
3. Resend's response is recorded back into the outbox row as `provider_message_id`. Failures classify per ADR §A.14.

This pattern means:
- Failed sends retry independently of the original transaction.
- Resend outages don't roll back business state.
- Email send log is queryable: "every email we sent for order X" = SELECT FROM outbox_event WHERE aggregate_id = X AND event_type = 'email.send'.

### Locale resolution

The caller resolves the locale before inserting the outbox row. For order-flow emails (paid, shipped, delivered): use the customer's `CountryCodePrimary` → `CountryConfiguration.DefaultLanguageCode`. For maker emails: use the maker user's primary country. For admin: always `cs-CZ` at launch.

### Unsubscribe / preferences

**Out of scope for MVP.** All transactional emails are required for the transaction; legal basis is contract performance. We do not send marketing email from the platform at launch.

A future ADR will introduce `email_preferences` per user when marketing email is added.

### Bounces / complaints

Resend webhook `/api/public/webhooks/resend` (with signature verification) records bounces and complaints into a `email_event` table keyed on `provider_message_id`. Hard bounces flag the user's email as undeliverable; subsequent order placement requires re-verifying the email. Soft bounces are logged but not actioned at MVP.

### Multi-country

`IEmailProvider` is per-provider, not per-country. Resend covers all our launch countries. `CountryConfiguration.DefaultEmailProvider` could switch to SendGrid or AWS SES per country if needed — same shape, new adapter.

## Alternatives considered

- **Templates in the database** — rejected. Templates change rarely; storing them in code keeps them under version control and reviewable. DB templates would also force a DB roundtrip per send.
- **Resend's dynamic templates (managed in their UI)** — rejected. Vendor lock-in; templates leave the Git history; hard to test locally.
- **React Email** — strong alternative. Components are nice, type-safe, hot-reloadable. Rejected only because it requires Node tooling in the .NET build pipeline; MJML has a precompiled CLI binary that's simpler to invoke from MSBuild. If frontend developers complain about MJML, revisit.
- **No outbox; direct send** — rejected. Same reasoning as ADR 0016: outbox decouples business state from external service availability.
- **Plain HTML templates without MJML** — rejected. Email-client compatibility is a real problem; MJML solves it.

## Consequences

### Positive
- Templates are Git-versioned, reviewable, locally renderable, and locale-aware.
- Send failures don't roll back transactions.
- Switching to SendGrid or SES is a new adapter + config row.
- All sent emails are queryable as a history per aggregate (via outbox).

### Negative
- MJML build step is one more thing in the pipeline. Mitigation: compiled HTML committed so CI without MJML still builds.
- Templates per locale will balloon as we add countries; mitigated by template count being bounded (~10 templates per locale).
- Bounce handling is minimal at MVP; can produce noise if users mistype emails. Tolerated; admin can review the `email_event` table.

## Compliance / verification

- SecOps: Resend API key in Key Vault; webhook signature verified.
- Reviewer: no direct calls to `IEmailProvider.SendAsync` from handlers — only the outbox processor.
- Reviewer: every new email template has both `cs-CZ` and the meta.json fields; CI fails if a `*.mjml` lacks a matching compiled `*.html`.
- Integration test: failed send schedules a retry in the outbox.

## Related

- Patterns: §A.14 error classification, §A.15 provider adapter, §A.20 idempotent webhooks (Resend webhooks for bounces)
- Roles: `docs/architecture/roles/email-provider.md` (to be authored)
- ADR 0016 (outbox pattern shared with payments)
