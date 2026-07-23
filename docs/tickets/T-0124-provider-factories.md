---
id: T-0124
title: Migrate IEmailProvider + ICompanyRegistry to keyed-services-with-factory (ADR 0008 alignment)
status: in_review
size: M
owner: dotnet-backend
created: 2026-07-23
updated: 2026-07-23
depends_on: [T-0065, T-0032, T-0028, T-0108]
blocks: []
user_stories: []
adrs: [0008, 0004]
phase: 4
manual_steps: []
security_touching: false
layers: [dotnet-backend]
---

# T-0124 — Provider factories for email + company registry

## Context

T-0065 introduced the keyed-services-with-factory pattern for
`IPaymentProvider` and T-0070 followed for `IShippingCarrier`, but the other
two provider seams stayed direct-DI: `SendGridEmailProvider` bound straight to
`IEmailProvider` and `AresCompanyRegistry` straight to `ICompanyRegistry`.
That leaves ADR 0008's "provider adapter + factory" mandate half-applied, and
`ProviderRegistry` (T-0108's write-time validation of admin-entered provider
codes) had to hard-code static `{"ares"}` / `{"sendgrid"}` fallbacks with a
`TODO(T-0124)` marker. This ticket completes the pattern so all four provider
seams read identically and a second country's registry/email provider plugs in
without touching handlers.

## Scope

- **`ICompanyRegistryFactory`** (Core.Domain/Registry) +
  **`CompanyRegistryFactory`** (Infra.Clients/Ares) — mirrors
  `PaymentProviderFactory` line-for-line: reads
  `CountryConfiguration.DefaultRegistry`, 5-minute `IMemoryCache` TTL on the
  code-only lookup, resolves the keyed `ICompanyRegistry`, typed
  NotFound/Configuration failures.
- **`IEmailProviderFactory`** (Core.Domain/Email) +
  **`EmailProviderFactory`** (Infra.Clients/SendGrid) — same shape over
  `CountryConfiguration.DefaultEmailProvider`.
- **Keyed registrations**: `AddKeyedScoped<ICompanyRegistry,
  AresCompanyRegistry>("ares")` (no unkeyed alias — both consumers migrated)
  and `AddKeyedSingleton<IEmailProvider, SendGridEmailProvider>("sendgrid")`
  **plus an unkeyed alias delegating to the keyed singleton** (see Locked
  decisions).
- **Consumer migration**: `RegisterMaker.Handler` resolves via
  `companyRegistryFactory.ResolveAsync(command.CountryCodePrimary)`;
  `RefreshMakerFromAres.Handler` via `maker.CountryCode`. Factory failures
  pass through as the handler result.
- **`ProviderRegistry`**: static Registry/Email fallback sets replaced with
  the same keyed-`ServiceDescriptor` probe payment + shipping already use —
  closes the `TODO(T-0124)`.
- **Error codes**: `CompanyRegistryNotRegistered`
  (`company.registryNotRegistered`) + `EmailProviderNotRegistered`
  (`email.providerNotRegistered`), both added to the `T8_NO_KEY_REQUIRED`
  allowlist per their sections' existing convention (generic fallback copy /
  log-only).

## Locked decisions

- **Email send path keeps the unkeyed `IEmailProvider` alias.** The outbox
  contract carries `LanguageCode` but **no recipient country** — per-send
  factory resolution would force a `CountryCode` field onto every email
  payload (T-0067/T-0072/… contract churn) for zero MVP behavior change
  (CZ-only). The alias delegates to the *same* keyed singleton, so there is
  exactly one SendGrid instance; when multi-country launch adds `CountryCode`
  to payloads (next to `LanguageCode`, T-0028's own precedent), the send path
  switches to the factory without DI re-plumbing. Documented on
  `IEmailProviderFactory` §Send-path note.
- **Factories live next to their lone adapters** (`Infra.Clients/Ares/`,
  `Infra.Clients/SendGrid/`) exactly as `PaymentProviderFactory` lives in
  `Comgate/` — they move to shared folders when a second adapter of the kind
  lands (the payment factory's own documented plan).
- **Dedicated NotRegistered codes** (mirroring payments) rather than
  shipping's reused `ShippingCarrierConfigurationError` — the payments shape
  is the pattern the ticket names as template.

## Out of scope

- Second registry/email adapters (ORSR, SES, …) — this ticket builds the seam.
- Payload-level `CountryCode` for emails (multi-country launch work).
- Moving `PaymentProviderFactory` to a shared folder (its doc defers that to
  the second payment provider).

## Acceptance criteria

- **AC-1** Given the CZ country configuration (`DefaultRegistry = "ares"`),
  when `RegisterMaker` or `RefreshMakerFromAres` runs, then the ARES adapter
  is resolved through the keyed factory and behavior is unchanged
  (all pre-existing handler tests pass unmodified except DI wiring).
- **AC-2** Given a country whose `DefaultRegistry`/`DefaultEmailProvider`
  names an unregistered code, when the factory resolves, then it returns the
  typed `Configuration` failure (`company.registryNotRegistered` /
  `email.providerNotRegistered`) instead of throwing.
- **AC-3** Given the admin `UpdateCountryConfiguration` validation
  (T-0108), when it asks `IProviderRegistry` for Registry/Email codes, then
  the answer comes from the actual keyed registrations (no static list) —
  registering a new adapter automatically makes its code admin-assignable.
- **AC-4** Given the email pipeline, when any outbox email sends, then
  exactly one `SendGridEmailProvider` instance serves both the keyed and the
  unkeyed (alias) resolution paths.

## Test plan reference

10 new tests: `CompanyRegistryFactoryTests` + `EmailProviderFactoryTests`
(5 each, mirroring `PaymentProviderFactoryTests`: happy path, cache-once,
unknown-code Configuration failure, unknown-country NotFound, blank-country
NotFound). Handler tests updated to route a factory substitute onto the
existing registry substitutes. Full unit suite 1875/1875 green.
`check-consistency.mjs` finding set byte-identical to master (line-number
shifts only).

## Status log

- 2026-07-23 `draft → in_progress → in_review` — built while the T-0153 walk
  waits on the operator's email-confirmation click; PR opened and left for
  operator merge (agent self-merge intentionally not exercised).
