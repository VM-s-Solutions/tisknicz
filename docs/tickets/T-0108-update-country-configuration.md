---
id: T-0108
title: UpdateCountryConfiguration admin command (VAT / fee / providers / invoicing mode)
status: ready
size: M
owner: dotnet-backend
created: 2026-06-14
updated: 2026-06-14
depends_on: [T-0105]
blocks: []
user_stories: [US-admin-0006]
adrs: [0004, 0013, 0014]
phase: 6
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-admin]
---

# T-0108 — UpdateCountryConfiguration admin command (VAT / fee / providers / invoicing mode)

## Context

T-0108 is the **third ticket in the admin-control bundle** (risk-ascending order: T-0111 read-only audit/orders/invoices queries → T-0109 outbox retry/acknowledge → **T-0108 config-row mutation** → T-0110 GDPR hard-delete). All four ship under one PR with sequential implementation. T-0108 is the first command in the bundle that **mutates the platform-behaviour-driving `CountryConfiguration` row** — the per-country control plane (ADR 0004 / patterns §A.12) that every domain service consults instead of branching on country. A bad write here silently changes VAT math, platform-fee splits, default shipping price, invoicing mode, and provider selection for **every subsequent order in that country**. That is why this ticket is `security_touching: true` and why the provider-change path carries a retype interlock.

This ticket directly satisfies **US-admin-0006 — Edit country configuration**: AC-1 (admin updates VAT rates, default provider codes, invoicing mode, etc. atomically with an audit entry), AC-2 (changing a `Default*Provider` to an unregistered keyed-service code is rejected with `country.providerNotRegistered`), AC-3 (changing the default payment provider requires retyping the new provider code — a high-stakes confirmation), and AC-4 (the next request reads the new values — no cache delay beyond per-request; the provider factories already cache the config for a short TTL per T-0065, so the only follow-up is that admin edits are rare and the short TTL is acceptable).

The entity mutators **already exist** on master (`CountryConfiguration.UpdateVatRates`, `UpdateInvoicingMode`, `UpdatePlatformFeeRate`, `UpdateDefaultShippingPrice`, `UpdateProviders` — all return `this` for chaining, all guard programmer-error inputs with `ArgumentException`). T-0108's job is to wrap them in a single command-layer feature: a Validator that surfaces user-input failures as clean 400s (the entity's `ArgumentException` guards are belt-and-braces, not the messaging path), a Handler that loads the row via `ICountryConfigurationRepository.GetByCodeAsync`, enforces the **provider-retype gate** and **unregistered-code rejection** before applying any mutator, and rides the `IAdminAuditableCommand` pipeline for the before/after JSONB audit row (ADR 0014). The read-side repository has `GetByCodeAsync` but **no `Update` method** — EF change-tracking on the loaded entity carries the mutation, and the `UnitOfWorkPipelineBehavior` commits; no explicit repository `Update` call and **no `SaveChangesAsync()` in the handler**.

**Provider-code validation seam.** The entity itself does NOT know whether a provider code is registered (`country-configuration.md` role doc: *"Whether a provider code is registered — that's the DI container's concern"*). Validation must query the DI registry at write time. Today `IPaymentProvider` (T-0065) and `IShippingCarrier` (T-0070) are keyed-registered; `IEmailProvider` (SendGrid) and the registry (ARES) are **not yet keyed** — their keyed migration is deferred to T-0124 (`AddMakablesClients.cs:204` comment). T-0108 introduces a small domain abstraction `IProviderRegistry` (queried at write time) whose Infra implementation probes the keyed container for payment + shipping and falls back to a static known-codes set (`{ "sendgrid" }`, `{ "ares" }`) for email + registry until T-0124 keys them. This keeps the handler clean (no `IServiceProvider` reach-through in `Core.AppServices`) and gives T-0124 one place to delete the static fallback.

**In-flight orders are NOT blocked by a provider change.** Per Q-C, an existing order in `PendingPayment / Paid / Accepted / Shipped` holds its own cached `PaymentProviderRef` / `PaymentRedirectUrl` (snapshotted at payment-session creation per T-0065). Switching `DefaultPaymentProvider` does not orphan those orders — their refund/capture flows still resolve the provider they were created against because the gateway transaction id is self-describing. So the handler **warns, does not block**: the Response carries an advisory `InFlightOrderCount` so the admin UI can surface *"N in-flight orders keep their current payment provider"* without rejecting the save. This mirrors the `UpdateDefaultShippingPrice` doc-comment on the entity (existing orders hold a pricing snapshot and are unaffected).

**Silent Success on a true no-op.** If every editable field in the command equals the row's current value (the admin opened the form, changed nothing, hit save), the handler returns 200 without calling any mutator. Per **Q-0021 (architect-ruled this engagement)**, the audit pipeline still writes a benign *"admin attempted country.update"* row — that is itself audit-worthy ("who touched this and when") and is NOT a defect. The previously-circulated "no second audit row on no-op" AC wording is **dropped platform-wide** (retroactively softens T-0103 AC-3); `AdminAuditPipelineBehavior` is unchanged (it correctly writes on every success). The no-op path skips mutators and the provider gate (no provider field changed → no retype required), but the audit row still rides the pipeline.

No migration ships (the entity + columns exist on master). Two-to-three new `country.*` error codes ship in `BusinessErrorMessage` with parallel `cs-CZ` i18n keys. NSwag regen targets the **admin host only**.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the bundle's cross-cutting dimensions at the 2026-06-14 deliberation (Q-A…Q-E); the four T-0108-specific dimensions (Q-C provider-change confirmation + in-flight handling) plus PM-absorbed bundle defaults follow.

### A. User-locked at the 2026-06-14 deliberation (non-negotiable)

1. **Provider-change confirmation = retype the new provider code (Q-C, US-admin-0006 AC-3).** When the command changes *any* `Default*Provider` field, the command must carry `ConfirmedProviderCode` equal to the **new** value of the changed provider field; mismatch → `country.providerConfirmationMismatch`. The retype is the high-stakes interlock — it mirrors the T-0110 email-retype idiom (admin must physically re-enter the value, not just click "yes"). **Rejected:** a boolean `confirmProviderChange` flag (one mis-click confirms; provides no proof the admin actually read the new code); a separate two-step confirm endpoint (heavier; the retype-in-payload is one round-trip and equally safe).

2. **Unregistered provider codes are rejected at write time against the DI registry (Q-C, US-admin-0006 AC-2).** Any `Default*Provider` value that does not match a registered keyed-service key (payment/shipping) or a known static code (email/registry, until T-0124) → `country.providerNotRegistered`, change rejected, nothing mutated. **Rejected:** accept-and-defer (let the bad code blow up at the next order's `ResolveAsync` — but that fails an unrelated customer's checkout hours later with no trace back to the admin edit; fail-at-write is the only defensible posture for a control-plane row).

3. **In-flight orders keep their cached provider refs — WARN, don't block (Q-C).** A provider change surfaces `Response.InFlightOrderCount` (orders in `PendingPayment / Paid / Accepted / Shipped`) as an advisory; the save proceeds. **Rejected:** block the change while in-flight orders exist (over-cautious — those orders carry self-describing gateway transaction ids and resolve their original provider regardless of the default; blocking would strand the admin behind unrelated orders); silently change with no advisory (loses the admin's situational awareness).

4. **Silent Success accepts the Q-0021 no-op audit row.** All-values-unchanged → 200, no mutators, no provider gate; the audit pipeline still writes the benign "attempted" row. **Rejected:** suppress the audit row on no-op (Q-0021 ruled this unattainable and undesirable — the attempt is itself audit-worthy); reject the no-op with a "nothing changed" error (a no-op save is not a user error).

### B. ADR-locked (no relitigation)

- **ADR 0004 (CountryConfiguration is the per-country control plane).** The command mutates the seeded row; it never inserts a new country (country creation is a seed migration per the role doc lifecycle). Code never branches on country — the handler operates on whatever row `GetByCodeAsync` returns.
- **ADR 0013 (per-audience JWT + scoped repositories).** The endpoint runs under the `Web.Admin` host audience; a customer/maker JWT cannot replay here. `CountryConfiguration` is a global control-plane row (not tenant-scoped), so the read uses `GetByCodeAsync` directly — no `ForCustomer`/`ForMaker` scoping applies. The fail-closed session check (no "system" attribution on a control-plane mutation) mirrors `RefundOrder` Step 1.
- **ADR 0014 (admin audit pipeline + UoW).** `Command : IAdminAuditableCommand` → `AdminAuditPipelineBehavior` captures before/after JSONB + `Reason`; `UnitOfWorkPipelineBehavior` commits the mutation + audit row atomically. The Handler **never** calls `SaveChangesAsync()`. The provider-change advisory (`InFlightOrderCount`) is a read inside the handler, not a mutation.
- **One-file feature shape.** `Features/CountryConfigurations/UpdateCountryConfiguration.cs` contains nested `Command`, `UpdateCountryConfigurationResponse`, `Validator`, `Handler`.
- **`BusinessResult<T>` for expected failures.** Provider-gate + unregistered-code + config-not-found surface as typed `BusinessResult.Failure` with `country.*` codes; Validator clamps (VAT/fee ranges, empty provider codes, reason length) surface as 400 via the existing validation envelope.

### C. PM-absorbed (no user input needed; bundle defaults)

- **`IAdminAuditableCommand` implementation.** `ActionCode => "country.update"`, `TargetEntity => "countryConfiguration"`, `TargetId => CountryCode`, `Notes => Reason`. Before/after JSONB auto-captured by the pipeline.
- **Fail-closed admin session.** Handler Step 1: `if (string.IsNullOrEmpty(session.GetUserId())) return Failure(Error.Unauthorized());` — a control-plane mutation must never be attributed to "system" (RefundOrder precedent).
- **Reason cap 2000 chars** (audit-log notes column width; RefundOrder m-3 precedent). `NotEmpty` + `MaximumLength(2000)`.
- **No outbox, no email.** A config edit emits nothing to the outbox and sends no email (Q-C / bundle default — T-0108 + T-0109 emit no outbox/email).
- **VAT / fee ranges (basis points).** `StandardVatRateBp` ∈ [0, 10000]; `ReducedVatRateBp` (nullable) ∈ [0, 10000] **and** ≤ `StandardVatRateBp` when set; `PlatformFeeRateBp` ∈ [0, 10000]; `DefaultShippingPriceMinor` ≥ 0. Matches the entity's own `Argument*` guards (the Validator is the user-input messaging layer; the entity guard is the programmer-error backstop).
- **Provider codes non-empty.** All four `Default*Provider` strings `NotEmpty` + `MaximumLength(64)`. `InvoicingMode` `IsInEnum()`.
- **`IProviderRegistry` domain seam.** New `Core.Domain/Configuration/IProviderRegistry.cs` exposing `IReadOnlySet<string> GetRegisteredCodes(ProviderKind kind)` for `Payment | Shipping | Registry | Email`. Infra impl probes the keyed container (payment/shipping) + static fallback (`{ "sendgrid" }`, `{ "ares" }`) for email/registry until T-0124. The handler depends on the abstraction, not `IServiceProvider`.
- **Globally-unique Response name:** `UpdateCountryConfigurationResponse` (post-PR-#38 NSwag convention).
- **DI registration:** `services.AddScoped<IProviderRegistry, ProviderRegistry>();` in the existing Infra registration block. (`ICountryConfigurationRepository` is already registered.)
- **Admin authorization:** `[Authorize]` (admin scheme). NSwag regen admin host only.
- **`security_touching: true`** — surfaced to secops Gate 3. **Q-0011 note (TOUCHED not closed):** the admin surface is admin-JWT-gated (2 trusted users) so the rate-limit/abuse concern Q-0011 raised against the customer surface is lower-risk here; keep Q-0011 open as a standalone secops follow-up; do NOT expand T-0108 scope to address it. Flag for secops Gate 3 re-confirmation.

## Scope

### Domain layer

- **`Core.Domain/Configuration/ProviderKind.cs`** — NEW enum:
  ```csharp
  public enum ProviderKind
  {
      Payment = 0,
      Shipping = 1,
      Registry = 2,
      Email = 3,
  }
  ```
- **`Core.Domain/Configuration/IProviderRegistry.cs`** — NEW interface:
  ```csharp
  /// <summary>
  /// Write-time validation seam for CountryConfiguration provider codes.
  /// The entity does not know whether a code is registered (role doc:
  /// "that's the DI container's concern"); this abstraction surfaces the
  /// registered keys without leaking IServiceProvider into Core.AppServices.
  /// </summary>
  public interface IProviderRegistry
  {
      IReadOnlySet<string> GetRegisteredCodes(ProviderKind kind);
  }
  ```
- **`CountryConfiguration.cs`** — **unchanged** (mutators already exist: `UpdateVatRates` :204, `UpdateInvoicingMode` :215, `UpdatePlatformFeeRate` :221, `UpdateDefaultShippingPrice` :237, `UpdateProviders` :247). No new method needed.
- **`ICountryConfigurationRepository.cs`** — **unchanged** (`GetByCodeAsync` returns the tracked entity; EF change-tracking carries the mutation; no `Update` method needed — the UoW pipeline commits).

### AppServices layer

- **`Core.AppServices/Features/CountryConfigurations/UpdateCountryConfiguration.cs`** — NEW one-file feature:
  - `Command(string CountryCode, int StandardVatRateBp, int? ReducedVatRateBp, InvoicingMode InvoicingMode, int PlatformFeeRateBp, long DefaultShippingPriceMinor, string DefaultPaymentProvider, string DefaultShippingCarrier, string DefaultRegistry, string DefaultEmailProvider, string? ConfirmedProviderCode, string Reason) : ICommand<UpdateCountryConfigurationResponse>, IAdminAuditableCommand`.
    - `ActionCode => "country.update"`; `TargetEntity => "countryConfiguration"`; `TargetId => CountryCode`; `Notes => Reason`.
  - `UpdateCountryConfigurationResponse(string CountryCode, int StandardVatRateBp, int? ReducedVatRateBp, InvoicingMode InvoicingMode, int PlatformFeeRateBp, long DefaultShippingPriceMinor, string DefaultPaymentProvider, string DefaultShippingCarrier, string DefaultRegistry, string DefaultEmailProvider, int InFlightOrderCount, bool ProviderChanged)` — globally-unique name. `InFlightOrderCount` is the advisory; `ProviderChanged` tells the UI whether a retype gate was applied.
  - `Validator : AbstractValidator<Command>`:
    - `CountryCode` — `NotEmpty` + `Length(2)` (ISO 3166-1 alpha-2).
    - `StandardVatRateBp` — `InclusiveBetween(0, 10000)`.
    - `ReducedVatRateBp` — when set: `InclusiveBetween(0, 10000)` **and** `<= StandardVatRateBp` (`When(c => c.ReducedVatRateBp.HasValue, ...)`).
    - `PlatformFeeRateBp` — `InclusiveBetween(0, 10000)`.
    - `DefaultShippingPriceMinor` — `GreaterThanOrEqualTo(0)`.
    - each `Default*Provider` — `NotEmpty` + `MaximumLength(64)`.
    - `InvoicingMode` — `IsInEnum()`.
    - `Reason` — `Cascade(Stop)` + `NotEmpty` + `MaximumLength(2000)`.
    - **Note:** the provider-retype gate + unregistered-code check are **NOT** Validator rules — they need the loaded row (to know which provider fields *changed*) and the DI registry (handler-injected). They live in the Handler.
  - `Handler(IUserSessionProvider session, ICountryConfigurationRepository configs, IProviderRegistry providerRegistry, IOrderQueries orderQueries, ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<UpdateCountryConfigurationResponse>>` primary-constructor DI. Steps (NO `SaveChangesAsync()`):
    1. **Fail-closed session** — `if (string.IsNullOrEmpty(session.GetUserId())) return Failure(Error.Unauthorized());`.
    2. **Load** — `var config = await configs.GetByCodeAsync(command.CountryCode, ct);` → `if (config is null) return Failure(Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));`.
    3. **Compute provider deltas** — determine which of the four provider fields differ from the loaded row (`paymentChanged`, `shippingChanged`, `registryChanged`, `emailChanged`); `providerChanged = any`.
    4. **No-op fast path (Silent Success, Q-0021)** — if NO field differs from the row (VAT, reduced, invoicing, fee, shipping price, all four providers all equal current), return `Success(BuildResponse(config, inFlightCount: 0, providerChanged: false))` WITHOUT touching mutators or the provider gate. The audit pipeline still writes the benign attempt row (Q-0021). (Cheap-first: compute `inFlightCount` only on the non-no-op path.)
    5. **Unregistered-code rejection (AC-2)** — for **every** provider field whose value changed, check `providerRegistry.GetRegisteredCodes(kind).Contains(newValue)`; first miss → `Failure(Error.Validation("default<Kind>Provider", BusinessErrorMessage.CountryProviderNotRegistered))`. (Validate even non-changed-from-default codes? No — only changed fields, so an admin editing VAT alone isn't blocked by a pre-existing-but-now-deprecated code. The check guards what the admin is *introducing*.)
    6. **Provider-retype gate (AC-3, Q-C)** — if `providerChanged`: require `ConfirmedProviderCode` to equal the **new** value of the changed provider field. When more than one provider field changed, the retype must match the **payment** provider's new value if payment changed, else the single changed field's new value (payment is the highest-stakes; US-admin-0006 AC-3 names the *default payment provider* specifically). Mismatch or null → `Failure(Error.Validation("confirmedProviderCode", BusinessErrorMessage.CountryProviderConfirmationMismatch))`. Ordering: this runs AFTER the unregistered check so an admin retyping a garbage code gets `providerNotRegistered` (the more actionable error), not a confirmation mismatch.
    7. **In-flight advisory** — `var inFlightCount = providerChanged ? await orderQueries.CountInFlightByCountryAsync(command.CountryCode, ct) : 0;` (in-flight = `PendingPayment | Paid | Accepted | Shipped`). WARN-only; never rejects.
    8. **Apply mutators** — chain the existing entity mutators for every changed group: `config.UpdateVatRates(...).UpdateInvoicingMode(...).UpdatePlatformFeeRate(...).UpdateDefaultShippingPrice(...).UpdateProviders(...);` (apply each unconditionally with the command values — they're idempotent for unchanged fields; the entity's `Argument*` guards are the backstop for any value the Validator somehow let through).
    9. **Return** — `Success(BuildResponse(config, inFlightCount, providerChanged));`. The UoW pipeline commits the tracked entity + the audit row.
  - `BuildResponse(CountryConfiguration, int inFlightCount, bool providerChanged)` private static helper projects the row's post-mutation field values + the two advisories.

- **`Core.Domain/Orders/IOrderQueries.cs`** — EXTEND with `Task<int> CountInFlightByCountryAsync(string countryCode, CancellationToken ct);` (in-flight = the four active states). This is read-side, AsNoTracking, admin-host unscoped (the count spans all makers/customers in the country). Implemented in `Infra.Database/Orders/OrderQueries.cs`.

### Infrastructure / Database layer

- **`Infra.Database/Configuration/ProviderRegistry.cs`** — NEW `IProviderRegistry` impl:
  - Primary-constructor DI: `ProviderRegistry(IServiceProvider services) : IProviderRegistry`.
  - `GetRegisteredCodes(ProviderKind kind)` returns:
    - `Payment` / `Shipping` — the registered keyed-service keys, read from `IServiceProviderIsKeyedService` / the registered `ServiceDescriptor` keys for `IPaymentProvider` / `IShippingCarrier` (mirror the `(d.ServiceKey as string)` discovery used in the webhook integration tests). At MVP this yields `{ "comgate" }` / `{ "packeta" }`.
    - `Registry` / `Email` — **static fallback** `{ "ares" }` / `{ "sendgrid" }` until T-0124 keys them. A `// TODO(T-0124): replace static fallback with keyed-container probe once IEmailProvider + ICompanyRegistry are keyed.` — *owner T-0124, tracked* (satisfies the "no TODO without owner" rule).
  - Case-insensitive set (`StringComparer.OrdinalIgnoreCase`) — provider codes are lowercase constants but admin input should match leniently.
- **`Infra.Database/Orders/OrderQueries.cs`** — implement `CountInFlightByCountryAsync`: `db.Orders.AsNoTracking().Where(o => o.CountryCode == countryCode && InFlightStates.Contains(o.State)).CountAsync(ct);` (soft-deleted excluded by the global Auditable filter; no `IgnoreQueryFilters`).
- **`Config/Extensions/AddMakablesInfrastructure.cs`** — register `services.AddScoped<IProviderRegistry, ProviderRegistry>();`. (`ICountryConfigurationRepository` + `IOrderQueries` already registered.)

### Web.Admin host

- **`Web.Admin/Controllers/CountryConfigurationsController.cs`** — NEW controller (mirrors `OrdersController` admin conventions):
  - `[ApiController] [ApiVersion("1.0")] [Route("api/v{version:apiVersion}/country-configurations")] [Authorize]`.
  - `UpdateCountryConfigurationRequest` body record (every editable field + `ConfirmedProviderCode` + `Reason`); the `countryCode` rides the route.
  - `[HttpPut("{countryCode}")]` action `Update(string countryCode, [FromBody] UpdateCountryConfigurationRequest request, CancellationToken ct)` — one-liner: `HandleResult(await Mediator.Send(new UpdateCountryConfiguration.Command(countryCode, ...request fields..., request.ConfirmedProviderCode, request.Reason), ct));`.
  - `[ProducesResponseType(typeof(UpdateCountryConfiguration.UpdateCountryConfigurationResponse), 200)]` + 400 / 401 / 404 envelopes for NSwag.

### Error codes + i18n

- **`Core.Domain/Common/BusinessErrorMessage.cs`** — `country.providerNotRegistered` **already exists** (`CountryProviderNotRegistered`, line 267) — reuse it. ADD:
  - `CountryProviderConfirmationMismatch = "country.providerConfirmationMismatch"` — the retyped `ConfirmedProviderCode` does not match the new provider value (AC-3 high-stakes interlock failed).
  - (`CountryConfigurationNotFound` already exists, line 275 — reuse for the load-miss.)
- **`frontend/src/lib/i18n/cs-CZ.ts`** — ADD parallel keys (no `country.*` keys exist there yet):
  - `'country.providerNotRegistered': 'Zadaný kód poskytovatele není zaregistrován v systému.'`
  - `'country.providerConfirmationMismatch': 'Pro potvrzení změny poskytovatele přepište nový kód přesně.'`
  - `'countryConfiguration.notFound': 'Konfigurace pro tuto zemi nebyla nalezena.'`

### Tests

#### UpdateCountryConfigurationHandlerTests (NEW, ~10 unit tests)

`backend/src/Makables.Tests/AppServices/Features/CountryConfigurations/UpdateCountryConfigurationHandlerTests.cs` — NSubstitute mocks (`IUserSessionProvider`, `ICountryConfigurationRepository`, `IProviderRegistry`, `IOrderQueries`). Seed a `CountryConfiguration` via `CountryConfiguration.Create(...)` as the loaded row. **TDD red-first — write these two pinning the pure predicates BEFORE any handler code:**

1. **Provider_change_without_matching_confirmation_is_rejected** (RED FIRST) — command changes `DefaultPaymentProvider` from `"comgate"` to `"comgate"`+registered alt, `ConfirmedProviderCode = null` (or a wrong value). Registry reports the new code registered. Assert: `Failure` with `CountryProviderConfirmationMismatch`; no mutator applied (row unchanged); `IOrderQueries` not invoked past the count or not at all if gate short-circuits.
2. **Unregistered_provider_code_is_rejected** (RED FIRST) — command changes `DefaultPaymentProvider` to `"stripe"`; `IProviderRegistry.GetRegisteredCodes(Payment)` returns `{ "comgate" }`. Assert: `Failure` with `CountryProviderNotRegistered`; rejection happens BEFORE the retype gate (so an unregistered+unconfirmed code returns `providerNotRegistered`, not the mismatch).
3. **Happy_path_provider_change_with_correct_retype_succeeds** — change payment provider to a registered alt; `ConfirmedProviderCode` == the new value. Assert: `Success`; `UpdateProviders` reflected in the response; `ProviderChanged == true`; `InFlightOrderCount` echoed from the mocked `CountInFlightByCountryAsync`.
4. **Happy_path_vat_only_change_no_provider_gate** — change `StandardVatRateBp` only, all providers unchanged, `ConfirmedProviderCode = null`. Assert: `Success`; no `providerNotRegistered`/mismatch; `ProviderChanged == false`; `InFlightOrderCount == 0` (count skipped on no-provider-change).
5. **No_op_all_values_unchanged_returns_success_without_mutation** — command equals the loaded row in every field. Assert: `Success`; no mutator invoked (verify via a spy/value-comparison); `IProviderRegistry` not queried; `IOrderQueries` not queried. (Q-0021: the audit row is the pipeline's job, not asserted here.)
6. **Fail_closed_when_session_has_no_user** — `session.GetUserId()` returns null/empty. Assert: `Failure` with `Error.Unauthorized()`; repository never loaded.
7. **Config_not_found_returns_not_found** — `GetByCodeAsync` returns null. Assert: `Failure` with `CountryConfigurationNotFound`.
8. **Reduced_vat_above_standard_is_rejected_by_validator** — run the Validator with `ReducedVatRateBp = 2200`, `StandardVatRateBp = 2100`. Assert `Validate().IsValid == false` on `ReducedVatRateBp`.
9. **Platform_fee_above_10000bp_is_rejected_by_validator** — `PlatformFeeRateBp = 10001`. Assert validation failure on `PlatformFeeRateBp`.
10. **Empty_provider_code_is_rejected_by_validator** — `DefaultPaymentProvider = ""`. Assert validation failure on `DefaultPaymentProvider`. (Plus a Validator case for `Reason` empty / >2000 chars and negative `DefaultShippingPriceMinor` folded in as cheap extra asserts.)

#### UpdateCountryConfigurationIntegrationTests (NEW, ~3 integration tests)

`backend/src/Makables.IntegrationTests/CountryConfigurations/UpdateCountryConfigurationIntegrationTests.cs` — Testcontainers Postgres + `WebApplicationFactory` + admin JWT + the seeded CZ `CountryConfiguration`.

1. **PUT_updates_vat_and_writes_audit_row** — admin PUTs new `StandardVatRateBp` + `ReducedVatRateBp` + `Reason`, no provider change. Assert 200; re-read the row → new VAT values persisted; exactly one `admin_audit_log` row with `action_code = "country.update"`, `before_json`/`after_json` reflecting the VAT delta, `notes` == reason.
2. **PUT_provider_change_with_wrong_retype_is_rejected_and_unchanged** — admin PUTs a registered alternate payment provider with a mismatched `ConfirmedProviderCode`. Assert 400 `country.providerConfirmationMismatch`; re-read the row → `DefaultPaymentProvider` unchanged; no mutation committed.
3. **PUT_provider_change_surfaces_in_flight_advisory_without_blocking** — seed 2 orders in `Paid`/`Accepted` for CZ; admin PUTs a registered alternate provider with the correct retype. Assert 200; response `inFlightOrderCount == 2`; row updated; the two orders' `PaymentProviderRef` unchanged in the DB (they keep their cached ref — Q-C).

### Docs

- **`docs/architecture/roles/country-configuration.md`** — under *Lifecycle → Modified by*, confirm the `UpdateCountryConfiguration.Command` reference is wired (T-0108). Note the `IProviderRegistry` write-time validation seam under *Invariants* (the DI-registry probe that backs the "must reference a registered keyed service" invariant).
- **`docs/tickets/INDEX.md`** — PM flips T-0108 to `**done**` post-merge.

### NSwag regen

The new `PUT /api/v1/country-configurations/{countryCode}` endpoint is a contract change → **NSwag regen REQUIRED in the same PR (admin host client only).** Per the pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff exposing `UpdateCountryConfigurationResponse` + the request shape + `InvoicingMode` + `ProviderKind` (if surfaced). The bundle's other three tickets regen separately within the same PR.

## Alternatives Considered

- **Option A — Boolean `confirmProviderChange` flag instead of a retyped code.** *Rejected per A.1* — a boolean is one mis-click away from confirming a catastrophic provider swap and provides zero evidence the admin read the new code. The retype (US-admin-0006 AC-3, mirroring the T-0110 email-retype idiom) forces the admin to physically re-enter the exact value; a typo fails the gate instead of committing a wrong provider.
- **Option B — Accept unregistered provider codes and let them fail at the next `ResolveAsync`.** *Rejected per A.2* — the failure would surface hours later as an unrelated customer's checkout breaking with no trace back to the admin edit. A control-plane row that drives every order's provider selection must fail-at-write; the `IProviderRegistry` probe makes the bad code a clean 400 the admin sees immediately.
- **Option C — Block the save while in-flight orders exist.** *Rejected per A.3* — in-flight orders carry self-describing gateway transaction ids (`PaymentProviderRef` snapshotted at session creation, T-0065) and resolve their original provider regardless of the new default. Blocking would strand the admin behind orders that the change doesn't actually affect. WARN-with-`InFlightOrderCount` preserves situational awareness without a false dependency.
- **Option D — Reach `IServiceProvider` directly into the handler to probe keyed registrations.** *Rejected per C* — `Core.AppServices` must not depend on the DI container internals (it would leak Infra concerns into the application layer and make the handler untestable without a real container). The `IProviderRegistry` domain seam keeps the handler mockable and gives T-0124 one file to update when email/registry get keyed.
- **Option E — Add a dedicated `Update`/`Replace` method on `ICountryConfigurationRepository`.** *Rejected* — `GetByCodeAsync` returns a tracked entity; EF change-tracking on the existing mutators carries the mutation and the `UnitOfWorkPipelineBehavior` commits. An explicit `Update` would be redundant and risk a double-tracked entity. (Same posture as every other admin mutation command in the bundle.)
- **Option F — Suppress the audit row on a true no-op save.** *Rejected per A.4 / Q-0021* — the architect ruled the "no second audit row" AC unattainable and the no-op attempt itself audit-worthy ("who opened this control-plane form and when"). The pipeline stays unchanged; the no-op path simply skips mutators and the provider gate.
- **Option G — Split into per-field commands (one for VAT, one for providers, one for invoicing mode).** *Rejected* — the admin form is a single save; per-field commands multiply the audit rows for one logical edit and complicate the retype gate (which is cross-field by nature: "did *any* provider change?"). The entity's per-group mutators already give the granularity; the command composes them in one atomic write.
- **Option H — Validate provider codes inside the FluentValidation Validator.** *Rejected* — the unregistered check needs the DI registry (handler-injected) and the retype gate needs the *loaded row* (to know which fields changed). Neither is available to a pure Validator. The Validator covers stateless input shape (ranges, non-empty, length); the handler covers stateful business rules.

## Out of scope

- **Creating a new country (`CountryConfiguration.Create`)** — countries are seeded via migration per the role-doc lifecycle; T-0108 only *updates* the existing row. A future "add country" admin flow is a separate ticket.
- **Editing locale / tax-ID-label / issuer / IBAN fields** — T-0108's editable set is exactly VAT rates, invoicing mode, platform fee, default shipping price, and the four provider codes (the US-admin-0006 AC-1 named set). Issuer name/IČO/DIČ/IBAN edits (T-0068b columns) are a separate downstream ticket; the seed/data-migration path covers them at MVP.
- **Keying `IEmailProvider` + `ICompanyRegistry` as keyed services** — deferred to **T-0124** (`AddMakablesClients.cs:204`). Until then `IProviderRegistry` uses the static `{ "sendgrid" }` / `{ "ares" }` fallback. T-0108 does not migrate them.
- **Migrating in-flight orders to a newly-selected provider** — explicitly NOT done (Q-C). In-flight orders keep their cached `PaymentProviderRef` / `PaymentRedirectUrl`; only NEW orders use the new default.
- **Cache invalidation beyond the existing short TTL** — the provider factories already cache the config for a short TTL (T-0065). US-admin-0006 AC-4 is satisfied by "no cache delay beyond per-request" for direct reads; the factory TTL is an accepted, documented eventual-consistency window for hot provider-resolution paths. A push-invalidation mechanism is out of scope.
- **Admin UI form** — the `/dashboard/admin/countries/{code}` Server Component + the retype confirmation modal are a frontend ticket (downstream); T-0108 ships only the backend endpoint + contract.
- **Q-0011 rate-limiting / abuse hardening of the admin surface** — TOUCHED not closed (see §C). Kept open as a standalone secops follow-up; not expanded here.

## Acceptance criteria

- **AC-1** Given an admin with a valid admin JWT, when they `PUT /api/v1/country-configurations/CZ` with new `StandardVatRateBp`, `ReducedVatRateBp`, `PlatformFeeRateBp`, `DefaultShippingPriceMinor`, `InvoicingMode`, and a `Reason` (no provider change), then the response is `200 OK`, the row reflects the new values on the next read, and exactly one `admin_audit_log` row is written with `action_code = "country.update"`, `before_json`/`after_json` capturing the delta, and `notes == Reason`. (US-admin-0006 AC-1.)
- **AC-2** Given the admin changes a `Default*Provider` to a code not registered as a keyed service (and not in the static email/registry fallback), when saved, then the response is `400 country.providerNotRegistered` and **nothing is mutated** (row unchanged on re-read). (US-admin-0006 AC-2.)
- **AC-3** Given the admin changes the `DefaultPaymentProvider` to a *registered* alternate, when `ConfirmedProviderCode` is null or does not equal the new provider value, then the response is `400 country.providerConfirmationMismatch` and nothing is mutated. (US-admin-0006 AC-3 — high-stakes retype interlock.)
- **AC-4** Given the same provider change, when `ConfirmedProviderCode` equals the new provider value exactly, then the response is `200 OK`, `DefaultPaymentProvider` is updated, and `response.providerChanged == true`.
- **AC-5** Given a provider change is committed, when the response is inspected, then `response.inFlightOrderCount` equals the count of orders for that country in `PendingPayment | Paid | Accepted | Shipped`; the save is **not** blocked by any in-flight orders, and those orders' `PaymentProviderRef` values are unchanged in the DB. (Q-C — WARN, don't block.)
- **AC-6** Given a VAT-only change (no provider field differs), when saved with `ConfirmedProviderCode = null`, then the response is `200 OK`, no provider gate fires, `response.providerChanged == false`, and `response.inFlightOrderCount == 0` (the count is skipped when no provider changed).
- **AC-7** Given the admin saves with every editable field equal to the current row values (a true no-op), then the response is `200 OK`, no entity mutator is invoked, and the audit pipeline still writes a benign `country.update` row (Q-0021 — the attempt is audit-worthy; this is not a defect).
- **AC-8** Given an unauthenticated request (no admin session user id resolvable), when the endpoint is called, then the response is `401 auth.required` and the repository is never loaded (fail-closed — no "system" attribution on a control-plane mutation).
- **AC-9** Given a request with `ReducedVatRateBp > StandardVatRateBp`, OR `PlatformFeeRateBp` ∉ [0, 10000], OR `StandardVatRateBp` ∉ [0, 10000], OR a negative `DefaultShippingPriceMinor`, OR an empty `Default*Provider`, OR a `Reason` that is empty or >2000 chars, when called, then the response is `400` with a FluentValidation error pointing at the offending field. Nothing is mutated.
- **AC-10** Given `PUT` targets a country code with no seeded `CountryConfiguration`, when called, then the response is `404 countryConfiguration.notFound`.
- **AC-11** Build clean. Unit tests: bundle baseline + ~10 new (`UpdateCountryConfigurationHandlerTests`), with the provider-confirmation-mismatch and unregistered-code predicates written **red-first** (TDD). Integration tests: baseline + ~3 new. `node scripts/check-consistency.mjs` exit 0 (no new violations vs the bundle baseline). NSwag regen committed in the same PR (admin host only); `frontend/src/lib/api-client/` types the new `PUT /country-configurations/{countryCode}` endpoint; no manual edits to the api-client folder (pre-commit hook enforces). Two new `country.*` i18n keys + the `countryConfiguration.notFound` key present in `cs-CZ.ts`, each matching a `BusinessErrorMessage` code.

## Technical notes

### Why the retype gate lives in the Handler, not the Validator

The retype gate is inherently stateful: it only fires when a provider field *changed*, which requires comparing the command against the *loaded row*. A FluentValidation Validator is stateless (it sees only the command). The unregistered-code check is likewise stateful — it needs the DI registry. The Validator's job is input shape (ranges, non-empty, length, enum membership); the Handler's job is the cross-field business rule ("did any provider change, and if so, was the change confirmed and registered?"). Splitting them keeps the Validator a pure function and the Handler the single place that reads collaborators.

### Why unregistered-code rejection runs before the retype gate

If an admin types a garbage provider code AND fails to retype it, two errors are technically true. The handler returns `providerNotRegistered` first because it is the more actionable message — "that provider doesn't exist" tells the admin exactly what to fix, whereas "your confirmation didn't match" is a downstream symptom of typing a code that was never going to work. The ordering is asserted in unit test #2.

### Why in-flight orders are not migrated

An order's payment lifecycle is bound to the gateway transaction it was created against. `PaymentProviderRef` (e.g. the Comgate `transId`) is captured at payment-session creation (T-0065) and is self-describing — a refund or capture resolves the provider from that ref, not from the country's *current* default. Switching `DefaultPaymentProvider` only changes which provider *new* payment sessions use. Migrating an in-flight order to a different gateway is meaningless (the money is already at the original gateway). Hence the advisory-only posture: surface the count so the admin knows N orders are mid-flight, but never block or rewrite them.

### Why `IProviderRegistry` is a domain seam (not a direct container probe)

`Core.AppServices` must not depend on `IServiceProvider` internals or `Microsoft.Extensions.DependencyInjection` keyed-service APIs — that would couple the application layer to the composition root and make the handler untestable without a live container. `IProviderRegistry` is declared in `Core.Domain.Configuration` and implemented in `Infra.Database`; the handler injects the interface and tests inject an NSubstitute mock returning a controlled key set. When T-0124 keys `IEmailProvider` + `ICompanyRegistry`, only the Infra implementation changes — the handler and its tests are untouched.

### Why no migration

Every column the command edits (`StandardVatRateBp`, `ReducedVatRateBp`, `InvoicingMode`, `PlatformFeeRateBp`, `DefaultShippingPriceMinor`, the four `Default*Provider` strings) already exists on the `CountryConfiguration` entity + table on master, and every mutator (`UpdateVatRates`, `UpdateInvoicingMode`, `UpdatePlatformFeeRate`, `UpdateDefaultShippingPrice`, `UpdateProviders`) already ships. T-0108 is pure application-layer composition plus one read-side count method — no schema change.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Configuration/ProviderKind.cs`
- `backend/src/Makables.Core.Domain/Configuration/IProviderRegistry.cs`
- `backend/src/Makables.Core.AppServices/Features/CountryConfigurations/UpdateCountryConfiguration.cs`
- `backend/src/Makables.Infra.Database/Configuration/ProviderRegistry.cs`
- `backend/src/Makables.Web.Admin/Controllers/CountryConfigurationsController.cs`
- `backend/src/Makables.Tests/AppServices/Features/CountryConfigurations/UpdateCountryConfigurationHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/CountryConfigurations/UpdateCountryConfigurationIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — add `CountryProviderConfirmationMismatch` (reuse existing `CountryProviderNotRegistered` + `CountryConfigurationNotFound`).
- `backend/src/Makables.Core.Domain/Orders/IOrderQueries.cs` — add `CountInFlightByCountryAsync`.
- `backend/src/Makables.Infra.Database/Orders/OrderQueries.cs` — implement `CountInFlightByCountryAsync`.
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IProviderRegistry`.
- `frontend/src/lib/i18n/cs-CZ.ts` — add `country.providerNotRegistered`, `country.providerConfirmationMismatch`, `countryConfiguration.notFound`.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (admin host); committed in the same PR.
- `docs/architecture/roles/country-configuration.md` — note the `IProviderRegistry` write-time seam; confirm the `UpdateCountryConfiguration.Command` modifier reference.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0108.md`.

## Status log

- 2026-06-14 `draft` by PM. Created as the **third ticket in the admin-control bundle** (risk-ascending: T-0111 read queries → T-0109 outbox retry/ack → T-0108 config mutation → T-0110 GDPR hard-delete; one PR). Reference precedents on master: `RefundOrder` (T-0105 — `IAdminAuditableCommand`, fail-closed session, Silent Success), `CountryConfiguration` entity + mutators (already on master), `PaymentProviderFactory`/`ShippingCarrierFactory` (T-0065/T-0070 — keyed-service registration the `IProviderRegistry` probe reuses). Scope: one-file `UpdateCountryConfiguration` feature + `ProviderKind` enum + `IProviderRegistry` domain seam + Infra impl + `CountInFlightByCountryAsync` read method + admin endpoint + 1 new error code + 3 i18n keys + ~10 unit tests (2 red-first) + ~3 integration tests. No migration, no outbox, no email.
- 2026-06-14 `draft → ready` by BA per the 2026-06-14 deliberation. User-locked: **Q-C** provider-change confirmation = retype the new provider code (US-admin-0006 AC-3) + reject unregistered codes (`country.providerNotRegistered`); in-flight orders keep their cached `PaymentProviderRef`/`PaymentRedirectUrl` — WARN (advisory `InFlightOrderCount`), don't block. **Q-0021** ruled (architect): accept no-op audit rows as benign "admin attempted X" records; dropped the "no second audit row" AC wording platform-wide; `AdminAuditPipelineBehavior` unchanged. PM-absorbed bundle defaults: `IAdminAuditableCommand` (before/after JSONB + reason) + fail-closed admin session; no `SaveChangesAsync` in handler; no outbox/email; reason cap 2000; Silent-Success on no-op (accepting the Q-0021 audit row); `security_touching: true`; NSwag regen admin host. **Q-0011** TOUCHED not closed — admin-JWT-gated surface, lower spam risk; kept open as a standalone secops follow-up; flagged for secops Gate 3 re-confirmation; scope NOT expanded. **Ready for dotnet-backend.** Implementer processes the bundle T-0111 → T-0109 → T-0108 → T-0110 sequentially in one branch / one PR.
