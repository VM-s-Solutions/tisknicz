---
id: 0019
title: Email — Resend; React Email templates compiled at build time; EmailProvider role; per-template versioning
status: accepted
date: 2026-05-21
deciders: [Architect]
---

# 0019 — Email (Resend)

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
