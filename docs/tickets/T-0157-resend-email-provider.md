---
id: T-0157
title: Switch the email provider to Resend (ADR 0019 re-amendment)
status: in_review
size: M
owner: dotnet-backend
created: 2026-07-23
updated: 2026-07-23
depends_on: [T-0028, T-0124]
blocks: []
user_stories: []
adrs: [0019, 0008, 0004]
phase: 7
manual_steps: [vendor-account, secret-rotation, deploy-trigger]
security_touching: true
layers: [dotnet-backend, dotnet-db, secops]
---

# T-0157 — Resend email provider

## Context

Operator directive: "Change email service to RESEND." The 2026-07-04
meeting's confirmed processor list (Q17) already names Resend, and the GDPR
page advertises it — T-0028's SendGrid amendment had drifted from the
business decision. The swap is the first real payoff of T-0124's
keyed-services-with-factory seam: a new adapter + a seed flip, no handler
changes.

## Scope

- **`ResendEmailProvider`** (`Infra.Clients/Resend/`, keyed `"resend"`) —
  `POST /emails` with Bearer key; sends the locally rendered
  `Subject` + `PlainTextBody` (placeholders are substituted in
  `EmailSendService`, never remotely); single optional attachment as
  base64; SendGrid-era failure taxonomy (5xx/429/408/transport →
  Transient for outbox retry, other 4xx → Permanent) and the T-0028
  sec-reviewer B-1 stance (upstream body never leaks into failures/logs).
- **`ResendOptions`** with ValidateOnStart (ApiKey, from-address, BaseUrl,
  retry bounds); named HttpClient + shared Polly registry pipeline
  (Mapbox/ARES per-options pattern).
- **DI**: `"resend"` keyed singleton is ACTIVE — the unkeyed
  `IEmailProvider` alias (EmailSendService's path) now delegates to it;
  SendGrid stays registered as an inactive keyed adapter (Comgate
  precedent — flipping back is a seed change).
- **Seed + data migration**: `CountrySeed` → `"resend"`;
  `SwitchDefaultEmailProviderToResend` migration flips existing rows
  (guarded `WHERE = 'sendgrid'`, reversible Down).
- **Secrets plumbing**: `Resend--ApiKey` in the Key Vault inventory
  (`key-vault.bicep`), `Resend__ApiKey` KV references for hosts +
  Functions (`main.bicep`), both deploy workflows (dev
  `ensure_secret` with `re_dev_boot_stub`; prod fail-closed
  `set_secret`) + verify-inventory lists; `Resend` section in the four
  hosts' `appsettings.Development.json`.
- ADR 0019 re-amended (transport → Resend; DB-translation half stays).

## Consequences (honest trade-offs)

- **Emails become plain-text.** SendGrid's hosted dynamic-template HTML is
  gone and the DB never stored HTML. Every email keeps its full content
  (subject + body were always rendered locally); a single branded HTML
  wrapper is a candidate follow-up ticket.
- `ProviderTemplateId`/`Data` on `EmailMessage` are ignored by Resend —
  contract intentionally unchanged so the seed rows and every
  `EmailSendService` branch keep working.
- The T-0028 SendGrid template IDs in the seed become inert until/unless
  the provider flips back.

## Acceptance criteria

- **AC-1** Given a fresh or migrated database, when the country config is
  read, then `DefaultEmailProvider = "resend"` and `ProviderRegistry`
  reports both `resend` and `sendgrid` as registered email codes
  (admin-assignable).
- **AC-2** Given any outbox email event, when `EmailSendService` sends,
  then the unkeyed `IEmailProvider` resolves to the Resend adapter and the
  request carries the rendered subject + text, Bearer key, and (for
  `order.paid.customerEmail`) the base64 invoice attachment.
- **AC-3** Given a Resend 5xx/429, when the send fails, then the outbox
  row retries (Transient); a 4xx parks it as Permanent; the upstream body
  appears in neither the result nor the logs.
- **AC-4** Given the dev deploy without a `RESEND_API_KEY` GitHub secret,
  when it runs, then the boot-stub keeps the hosts booting (emails fail at
  call time, loudly) — and setting the secret + redeploying replaces it.

## Manual steps (operator)

1. Create/confirm the **Resend account**, verify the `makables.cz` sending
   domain (SPF + DKIM records Resend shows you), and mint an API key.
2. Add the key as GitHub secret **`RESEND_API_KEY`** in the `dev`
   environment (repo Settings → Environments → dev) — and later in
   `production`.
3. Redeploy dev (any code merge or manual "Deploy → dev" run) so the key
   lands in Key Vault. Until the domain is verified, set
   `Resend:DefaultFromAddress` to `onboarding@resend.dev` via app
   settings if you want test mail flowing immediately.

## Test plan reference

8 new tests (`ResendEmailProviderTests`): request shape (from/to/subject/
text/reply_to, Bearer, base64 attachment), options from-address fallback,
transient/permanent taxonomy incl. PII-non-leak pin, unparsable-2xx
receipt, BaseUrl override. Full unit suite 1893/1893 green in Release;
`az bicep build` clean.

## Status log

- 2026-07-23 `draft → in_progress → in_review` — operator-directed switch;
  built on the T-0124 factory seam the same day it merged.
