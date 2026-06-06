# T-0067 — Extend MarkOrderPaid with outbox emission + payment_method persistence + WebhookPayload.PaidAt

**Phase:** 4 (Orders)
**Size:** M
**State:** `ready`
**Depends on:** T-0011 (`IOutbox` + outbox infrastructure), T-0028 (`EmailTemplate` + `IEmailSendService`), T-0029 (`ProcessOutboxFunction` + dispatcher), T-0066 (`MarkOrderPaid.Command` shape, webhook controller, `ComgatePaymentProvider.ParseAndVerifyWebhookAsync`)
**Owner:** `dotnet-backend`
**ADRs:** 0014 (audit + UoW pipeline), 0016 (Comgate webhook), 0019 (email pipeline — SendGrid), 0020 (outbox + retry policy)
**Stories:** US-customer-0010 AC-4 ("order received" email), US-maker-0006 ("new order arrived" email)
**Role doc:** [docs/architecture/roles/order.md](../architecture/roles/order.md), [docs/architecture/roles/payment-provider.md](../architecture/roles/payment-provider.md)

## Why now

T-0066 shipped the webhook + state transition stub:
- `MarkOrderPaid.Command` already carries `PaymentMethod` + `PaidAt` parameters, but the handler ignores them.
- The state transition via `Order.MarkAsPaid` succeeds; the order moves to `Paid`.
- No outbox events are emitted; the customer never gets an "order received" email; the maker never gets a "new order arrived" email; T-0068 invoice generation has no trigger.

Until T-0067 lands:
- **US-customer-0010 AC-4 silently broken** — the customer pays via Comgate, the order is `Paid` in the DB, but they get no email confirmation.
- **US-maker-0006 silently broken** — the maker has no idea a new order arrived. They'd only see it if they manually visited `/dashboard/maker`.
- **Admin reporting blind** — no `payment_method` column means admin can't differentiate "card payments" from "bank transfers" for reconciliation.
- The Comgate `PaidAt` timestamp is captured by `VerifyPaymentAsync` but discarded; the DB records `clock.UtcNow` from our webhook handler, which is ~seconds off the actual payment time.

T-0067 closes the gap end-to-end.

## Scope

### User decisions captured upfront (research workflow + synthesis)

1. **PaidAt source (Q1):** widen `WebhookPayload` to surface `PaymentStatus.PaidAt`; trust Comgate's PAID timestamp. The ~seconds gap between Comgate's recorded time and our clock is small but real; we want the customer-facing invoice to show the actual payment time. WebhookPayload gains a nullable `PaidAt?` field; `ComgatePaymentProvider.ParseAndVerifyWebhookAsync` propagates `verifyResult.Value.PaidAt`; `ComgateWebhookController` passes it into the Command.
2. **`invoice.generate` enqueue (Q2):** T-0067 does NOT enqueue the `invoice.generate` event. T-0068 will add the third `outbox.Enqueue` call when it ships, alongside its `GenerateInvoice` Function + dispatcher routing. Cleaner separation; no half-wired routing in master.
3. **EmailSendService payload generalisation (Q3):** per-event-type switch branch. `EmailSendService.SendAsync` switches on `outboxEventType` and deserializes the matching concrete payload type. Strict typing; each new event = one new case. Pattern is "add one case per new event."
4. **Action URL pattern (Q4):** pre-bake the full action URL into the outbox payload at enqueue time. `MarkOrderPaid.Handler` builds `$"{publicAppUrls.WebBaseUrl}/objednavka/{order.Id}"` and includes it as `ActionUrl` in the payload. `EmailSendService` passes it verbatim to SendGrid Dynamic Template as a substitution variable.

### Migration (`Infra.Database/Migrations/AddOrderPaymentMethod`)

Only `payment_method` is genuinely new — `paid_at` already exists from T-0060 (`Migrations/20260603110319_Orders.cs:36`). The migration name reflects that.

```csharp
public partial class AddOrderPaymentMethod : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "payment_method",
            table: "orders",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "payment_method", table: "orders");
    }
}
```

Length 40 matches `shipping_method` precedent. Comgate labels (`CARD_CZ`, `BANK_CZ_RB`, etc.) fit comfortably. No index — admin queries by `payment_method` are out-of-MVP analytics.

### Order entity update (`Core.Domain/Orders/Order.cs`)

- Add `public string? PaymentMethod { get; private set; }` next to `PaidAt`.
- Extend `MarkAsPaid` signature: `(IClock clock, string paymentProviderRef, string? paymentMethod, DateTimeOffset? paidAtOverride)`.
  - `PaidAt = paidAtOverride ?? clock.UtcNow;` — preserves T-0066's clock-wins semantic when caller has nothing; uses the caller's value when supplied.
  - `PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? null : paymentMethod.Trim();` — set-once with the same belt-and-braces pattern as `PaymentProviderRef` (Order.cs:444-449). A second call with a DIFFERENT non-null `paymentMethod` returns `OrderInvalidTransition` (refuses overwrite); a matching value or null succeeds.
- EF mapping in `OrderConfiguration.cs`: `builder.Property(o => o.PaymentMethod).HasColumnName("payment_method").HasMaxLength(40);`

T-0066's pinning test `MarkOrderPaidHandlerTests.cs:199-218` is updated, NOT deleted — the assertion shifts from "Command fields ignored" to "Command fields persisted."

### `WebhookPayload` record widening (`Core.Domain/Payments/WebhookPayload.cs`)

Add nullable `DateTimeOffset? PaidAt`:

```csharp
public sealed record WebhookPayload(
    string ProviderRef,
    PaymentState State,
    string? PaymentMethod,
    DateTimeOffset? PaidAt);
```

This is a public domain record — the change is technically a breaking ABI change, but the only producer is `ComgatePaymentProvider.ParseAndVerifyWebhookAsync` and the only consumer is `ComgateWebhookController`, both shipped in T-0066 and edited in this same PR. No external surface.

### `ComgatePaymentProvider.ParseAndVerifyWebhookAsync` update

Propagate `verifyResult.Value.PaidAt` into the returned `WebhookPayload`. One-line change:

```csharp
return BusinessResult.Success(
    new WebhookPayload(transId, status.State, status.PaymentMethod, status.PaidAt));
```

### `ComgateWebhookController` update

Pass `payload.PaidAt` into `MarkOrderPaid.Command`:

```csharp
var command = new MarkOrderPaid.Command(
    OrderId: order.Id,
    ProviderRef: payload.ProviderRef,
    PaymentMethod: payload.PaymentMethod,
    PaidAt: payload.PaidAt);   // <-- was null in T-0066
```

### `MarkOrderPaid.Handler` update

Two changes:

1. **Persist Command fields.** Call the extended `Order.MarkAsPaid(clock, providerRef, paymentMethod, paidAt)` (was 2-arg in T-0066).
2. **Enqueue 2 outbox events** (NOT 3 — `invoice.generate` deferred to T-0068 per Q2). Both share `order.Id` as `aggregateId` so the dispatcher can group them.

Handler primary-ctor DI gains:
- `IOutbox outbox`
- `IPublicAppUrls publicAppUrls` (existing; for `WebBaseUrl`)
- `ILanguageResolver languageResolver` (existing; per T-0028 pattern)
- Optional: `IMakerRepository makers` (to resolve maker email; or include `MakerId` in the payload and let the consumer resolve)

Final handler order:
1. Resolve session (unchanged).
2. Lookup order (unchanged).
3. Defence-in-depth refId check (unchanged).
4. `order.MarkAsPaid(clock, providerRef, paymentMethod, paidAt)` — extended signature.
5. Build customer payload + `outbox.Enqueue(order.Id, OrderPaidCustomerEmail, customerPayloadJson)`.
6. Resolve maker, build maker payload + `outbox.Enqueue(order.Id, OrderPlacedMakerEmail, makerPayloadJson)`.
7. Return `Response`. UoW pipeline commits Order + 2 outbox rows atomically per ADR 0014.

### New `OutboxEventTypes` constants

In `Core.Domain/Outbox/OutboxEventTypes.cs` (append to existing convention `<domain>.<action>.<modality>`):

```csharp
public const string OrderPaidCustomerEmail = "order.paid.customerEmail";
public const string OrderPlacedMakerEmail  = "order.placed.makerEmail";
```

Update `IsEmailSend(string eventType)` classifier to include both. **Do NOT add `InvoiceGenerate` here** — that's T-0068.

### New payload records (`Core.Domain/Outbox/`)

PascalCase JSON property names (matches `OneTimeTokenOutboxPayload.cs:22-27` convention + `SendEmailHandlerTests.cs:40` example). Both records carry `ActionUrl` pre-baked per Q4.

**`OrderPaidCustomerEmailPayload.cs`:**

```csharp
public sealed record OrderPaidCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    long TotalAmountMinor,
    string Currency,
    string LanguageCode,
    string ActionUrl);   // {WebBaseUrl}/objednavka/{OrderId}
```

**`OrderPlacedMakerEmailPayload.cs`:**

```csharp
public sealed record OrderPlacedMakerEmailPayload(
    string OrderId,
    string OrderNumber,
    string MakerId,
    string MakerEmail,
    long TotalAmountMinor,
    string Currency,
    string LanguageCode,
    string ActionUrl);   // {WebBaseUrl}/dashboard/maker/objednavky/{OrderId}
```

### Email template seed migration

Adds two new rows to `email_templates` + four to `email_template_translations` (cs-CZ + en-US each). Mirrors `Migrations/20260524190759_EmailTemplates.cs:142-159`:

- `EmailTemplateType.OrderPaidCustomer` (new enum value 4)
- `EmailTemplateType.OrderPlacedMaker` (new enum value 5)

Use `d-placeholder-order-paid-customer` and `d-placeholder-order-placed-maker` as the SendGrid Dynamic Template IDs (parallel to the auth-flow placeholder pattern at line 147-149). Real SendGrid template IDs land in a config change at deploy time per the standard pattern.

Czech wording for the email subjects (PM/UX may refine):
- `order.paid.customer`: subject `'Děkujeme za objednávku #{OrderNumber}'`
- `order.placed.maker`: subject `'Nová objednávka #{OrderNumber}'`

### `EmailSendService.SendAsync` per-event-type branch (Q3)

Current code (`IEmailSendService.cs:61`) hardcodes `JsonSerializer.Deserialize<OneTimeTokenOutboxPayload>`. T-0067 refactors:

```csharp
return outboxEventType switch
{
    OutboxEventTypes.AuthMagicLinkSend
        or OutboxEventTypes.AuthEmailConfirmationSend
        or OutboxEventTypes.AuthPasswordResetSend
        => await SendAuthEmail(outboxEventType, payloadJson, ct),

    OutboxEventTypes.OrderPaidCustomerEmail
        => await SendOrderEmail<OrderPaidCustomerEmailPayload>(
            outboxEventType, payloadJson, ct,
            extractEmail: p => p.Email,
            extractLanguage: p => p.LanguageCode,
            mapTemplate: () => EmailTemplateType.OrderPaidCustomer),

    OutboxEventTypes.OrderPlacedMakerEmail
        => await SendOrderEmail<OrderPlacedMakerEmailPayload>(
            outboxEventType, payloadJson, ct,
            extractEmail: p => p.MakerEmail,
            extractLanguage: p => p.LanguageCode,
            mapTemplate: () => EmailTemplateType.OrderPlacedMaker),

    _ => BusinessResult.Failure(Error.Permanent(BusinessErrorMessage.EmailEventTypeUnknown)),
};
```

`SendOrderEmail<T>` is a private generic helper that:
1. Deserializes the payload (Permanent failure on malformed).
2. Resolves template via `IEmailTemplateRepository.GetActiveAsync(templateType, languageCode)`.
3. Builds the SendGrid `Personalization` with template substitutions (`OrderNumber`, `TotalAmountMinor`, `Currency`, `ActionUrl`, `ContactName` for customer; `OrderNumber`, `TotalAmountMinor`, `Currency`, `ActionUrl` for maker).
4. Calls `IEmailProvider.SendAsync` and returns the `BusinessResult`.

`BuildActionUrl` (`IEmailSendService.cs:159-175`) is **NOT modified** — the order action URLs come pre-baked in the payload per Q4. The auth-flow tokenised path remains unchanged.

### New `BusinessErrorMessage` codes (`Core.Domain/Common/BusinessErrorMessage.cs`)

Under the existing `// === Email ===` block (or extending it):

- `OrderEmailPayloadMalformed = "email.orderPayloadMalformed"` — Permanent (consumer-side; means a producer enqueued a bad payload).

The existing `EmailEventTypeUnknown` covers the default case in the switch.

### Frontend i18n (`frontend/src/lib/i18n/cs-CZ.ts`)

1 new key (the consumer-side malformed-payload code surfaces in the admin audit log only — not customer-facing):

```ts
'email.orderPayloadMalformed': 'Vnitřní chyba při generování e-mailu k objednávce. Tým byl informován.',
```

The 2 new email subject + body strings live in the `email_template_translations` table (DB-backed), NOT in cs-CZ.ts — per T-0028's pattern.

### NSwag regen

No public contract changes — the webhook endpoint is server-to-server and the public client never calls anything new. Run the regen step but expect a near-zero diff.

### Tests

#### Unit — `Makables.Tests/`

- `Domain/Orders/OrderTests.cs` — extend the `MarkAsPaid` test cluster:
  - `MarkAsPaid_with_PaymentMethod_persists_the_value` — new positive test.
  - `MarkAsPaid_with_PaidAtOverride_uses_override_not_clock` — new positive test.
  - `MarkAsPaid_with_null_PaidAtOverride_falls_back_to_clock` — new positive test.
  - `MarkAsPaid_with_DIFFERENT_existing_PaymentMethod_returns_InvalidTransition` — set-once pin (mirrors the existing `MarkAsPaid_with_DIFFERENT_pre_set_PaymentProviderRef_trips_set_once` from T-0066).
  - `MarkAsPaid_with_matching_existing_PaymentMethod_succeeds` — set-once relaxation pin.
- `AppServices/Features/Orders/MarkOrderPaidHandlerTests.cs` — update + extend:
  - Update the existing pinning test: `Command_fields_PaymentMethod_PaidAt_now_persisted_via_extended_MarkAsPaid_signature` (was `…_NOT_persisted_at_T_0066`).
  - `Handler_enqueues_OrderPaidCustomerEmail_outbox_row` — NSubstitute verify on `IOutbox.Enqueue(order.Id, OrderPaidCustomerEmail, …)` with payload assertion.
  - `Handler_enqueues_OrderPlacedMakerEmail_outbox_row` — same shape, maker payload.
  - `Handler_does_NOT_enqueue_invoice_generate_yet` — explicit negative pin so T-0068's addition is the only diff.
  - `Handler_payloads_contain_pre_baked_action_urls` — verify the URLs.
  - `Handler_fails_idempotently_when_MarkAsPaid_returns_InvalidTransition` — order already Paid; handler returns failure without enqueueing.
- `AppServices/Features/Email/EmailSendServiceTests.cs` — extend:
  - `SendAsync_with_OrderPaidCustomerEmail_routes_to_OrderEmail_branch` (NSubstitute on `IEmailProvider`).
  - `SendAsync_with_OrderPlacedMakerEmail_routes_to_OrderEmail_branch`.
  - `SendAsync_with_malformed_order_payload_returns_OrderEmailPayloadMalformed_Permanent`.
  - `SendAsync_with_unknown_event_type_still_returns_EmailEventTypeUnknown` — auth-flow path unchanged.
- `Infra/Clients/Comgate/ComgatePaymentProviderWebhookTests.cs` — extend:
  - `ParseAndVerifyWebhookAsync_propagates_PaidAt_from_VerifyPaymentAsync` — pin Q1 plumbing.
  - `ParseAndVerifyWebhookAsync_with_null_PaidAt_from_VerifyPayment_returns_null_in_WebhookPayload`.

#### Integration — `Makables.IntegrationTests/Webhooks/ComgateWebhookTests.cs`

Extend the existing happy-path test:
- After POST → 200 + `order.State == Paid`:
  - Assert `order.PaymentMethod == "CARD_CZ"` (from the `FakeComgatePaymentProvider` script).
  - Assert `order.PaidAt == <fake-provider's PaidAt>` (not the test clock's `UtcNow`).
  - Assert **2 rows** exist in `outbox_events` for `aggregate_id == order.Id` with event types `order.paid.customerEmail` and `order.placed.makerEmail`.
  - Assert **NO row** with event type `invoice.generate` (Q2 negative pin).
  - Assert the payload JSONs deserialize cleanly.

Add a new test:
- `Idempotent_second_delivery_does_NOT_re_enqueue_outbox_events` — POST same webhook twice; second call returns 200 with idempotency short-circuit (T-0066 logic); only 2 outbox rows total.

### Docs

- Update `docs/architecture/roles/order.md` — note that `MarkAsPaid` now persists `PaymentMethod` + `PaidAt`; outbox emission listed under "Lifecycle > paid event."
- Update `docs/architecture/roles/payment-provider.md` — `WebhookPayload` shape now includes `PaidAt`.

## Acceptance criteria

- **AC-1** Migration `AddOrderPaymentMethod` adds `payment_method VARCHAR(40) NULL` to `orders`. `Down` drops it. Up + Down both verified on a clean DB.
- **AC-2** `Order` entity exposes `public string? PaymentMethod { get; private set; }` with EF mapping `HasColumnName("payment_method").HasMaxLength(40)`.
- **AC-3** `Order.MarkAsPaid` signature extended to `(IClock, string providerRef, string? paymentMethod, DateTimeOffset? paidAtOverride)`. `PaidAt` resolves to `paidAtOverride ?? clock.UtcNow`. `PaymentMethod` is set-once (different non-null value → `OrderInvalidTransition`; matching value or null → success).
- **AC-4** `WebhookPayload` record gains nullable `DateTimeOffset? PaidAt`. `ComgatePaymentProvider.ParseAndVerifyWebhookAsync` propagates `verifyResult.Value.PaidAt`. `ComgateWebhookController` passes it into `MarkOrderPaid.Command`.
- **AC-5** `MarkOrderPaid.Handler` calls the 4-arg `Order.MarkAsPaid` and persists the new fields. The existing T-0066 pinning test is **updated** (not deleted) to assert the new persistence semantics.
- **AC-6** `OutboxEventTypes` gains two constants: `OrderPaidCustomerEmail = "order.paid.customerEmail"`, `OrderPlacedMakerEmail = "order.placed.makerEmail"`. `IsEmailSend(string)` returns true for both. **`InvoiceGenerate` is NOT added — T-0068 owns it.**
- **AC-7** `MarkOrderPaid.Handler` enqueues exactly **2 outbox events** (NOT 3) with `aggregateId = order.Id`. Both payloads contain pre-baked `ActionUrl` per Q4. Order: customer email first, maker email second. Verified by NSubstitute.
- **AC-8** Two new payload records `OrderPaidCustomerEmailPayload` + `OrderPlacedMakerEmailPayload` exist in `Core.Domain/Outbox/` with PascalCase JSON property names (matches existing convention).
- **AC-9** `EmailTemplateType` enum gains `OrderPaidCustomer = 4` + `OrderPlacedMaker = 5`. Seed migration adds 2 rows to `email_templates` + 4 rows to `email_template_translations` (cs-CZ + en-US each) with `d-placeholder-*` SendGrid IDs.
- **AC-10** `EmailSendService.SendAsync` per-event-type switch handles `OrderPaidCustomerEmail` + `OrderPlacedMakerEmail` (deserialize the matching payload type → resolve template → send via `IEmailProvider`). Auth-flow path unchanged. Malformed order payload → `Error.Permanent(OrderEmailPayloadMalformed)`. Unknown event type → existing `EmailEventTypeUnknown`.
- **AC-11** `BuildActionUrl` (`IEmailSendService.cs:159-175`) is NOT modified. Order action URLs come pre-baked in the payload at enqueue time. Auth-flow tokenised path stays identical.
- **AC-12** 1 new `BusinessErrorMessage` code (`OrderEmailPayloadMalformed`) + 1 Czech i18n key in `cs-CZ.ts`. Email subjects + bodies live in `email_template_translations` (DB-backed).
- **AC-13** Architectural compliance: no `Console.*`; no `SaveChangesAsync()` in handler (UoW commits both order mutation + 2 outbox rows atomically); no `dynamic`; no inline error strings.
- **AC-14** Integration test extends `ComgateWebhookTests.POST_happy_path_transitions_order_to_Paid` to assert `payment_method` populated, `paid_at == fake-provider's PaidAt` (not clock's), 2 outbox rows with the right event types, NO `invoice.generate` row. New idempotency test asserts the second delivery does NOT re-enqueue.
- **AC-15** Test count: at least 14 new unit tests + 1 extended integration test + 1 new integration test. Build clean. Baseline post-T-0066 master = 1057 unit + 131 integration; target 1071+ unit + 133+ integration.

## Out of scope

- **`invoice.generate` enqueue** — T-0068 territory (Q2). T-0068 adds the third `outbox.Enqueue` call + the `IsInvoiceGenerate` classifier + `IOutboxQueuePublisher.PublishGenerateInvoiceAsync` + the `GenerateInvoice` Function. T-0067 has a `// T-0068: enqueue invoice.generate here` comment in the handler.
- **Real SendGrid template provisioning** — `d-placeholder-*` IDs ship in the seed migration; real template IDs land in a deploy-time config change (Azure App Service Configuration override).
- **Customer/maker email content design** — UX provides the SendGrid template body separately; T-0067 only ships the payload contract + the placeholder IDs.
- **Refund email** — T-0105 territory.
- **Maker dashboard notification** — T-0067 emits ONLY emails; in-app notifications are a separate feature (post-MVP).
- **`payment_method` index for analytics** — out-of-MVP. Admin can `SELECT payment_method, COUNT(*) FROM orders GROUP BY payment_method` for ad-hoc reports without an index at MVP volume.

## Technical notes

### Why widen `WebhookPayload` instead of widening `MarkOrderPaid.Command` only

The Command already accepts `PaidAt`; T-0066 just hardcoded `null` in the controller. The cleanest fix is to surface the existing `PaymentStatus.PaidAt` from `VerifyPaymentAsync` through to the controller. `WebhookPayload` is the natural carrier — it's the value record returned by `ParseAndVerifyWebhookAsync`. Future webhook implementations (Stripe, GoPay) get the same plumbing for free.

### Why pre-bake the action URL instead of computing at send time (Q4)

The producer (`MarkOrderPaid.Handler`) has access to `IPublicAppUrls.WebBaseUrl` and `order.Id` — building `$"{webBaseUrl}/objednavka/{order.Id}"` is one line. The consumer (`EmailSendService`) is a stateless drainer; it should NOT know about the URL convention. Pre-baking also means: if `WebBaseUrl` changes config mid-flight, the enqueued email locks in the old URL — which is the correct behaviour (the customer's email at-time-of-payment used the old URL; we don't retroactively rewrite it).

### Why per-event-type switch instead of polymorphic deserialization (Q3)

Polymorphism via `JsonPolymorphism` requires a `$type` discriminator in the JSON. Adding that to every payload + maintaining the converter registration is more surface than the switch. The switch is also strictly typed at compile time — adding a new event type without a case label is a compile error (after the discriminated handler dispatches). Trade-off: more boilerplate per new event type, but each is local to one switch arm.

### Why 2 outbox rows in one transaction (atomicity)

`UnitOfWorkPipelineBehavior` commits the entire MediatR pipeline in a single Postgres transaction per ADR 0014. The order mutation (`State → Paid`, `PaymentProviderRef`, `PaymentMethod`, `PaidAt`) AND the 2 outbox rows ship atomically. If anything fails, nothing commits; the webhook returns a failure and Comgate retries; we never have "order is Paid but no email queued" or vice versa.

### Why `EmailSendService` deserializes (not the dispatcher)

Per ADR 0020, the dispatcher (`ProcessOutboxDispatcher`) routes by event type and publishes the queue message — it never deserializes the payload. The drainer-side handler (`SendEmailHandler` → `IEmailSendService`) owns deserialization. T-0067 follows that convention: the switch lives in `EmailSendService.SendAsync`, not in the dispatcher.

### Why `LanguageCode` is in the payload (not resolved at send time)

The customer's preferred language is captured at enqueue time (from `IUserSessionProvider` → `IUserRepository.LanguageCode`). If their preference changes between enqueue and drain (rare), they get the email in the language they had when the order was paid — which matches the "frozen-at-checkout" UX contract. Same reason as the URL pre-bake.

### Why `OrderEmailPayloadMalformed` is a new error code (not generic `EmailEventTypeUnknown`)

`EmailEventTypeUnknown` means "the dispatcher routed an event we don't recognize" — a configuration drift between producer and consumer. `OrderEmailPayloadMalformed` means "we recognize the event but the payload JSON is broken" — a producer bug. They should fire different alerts.

### Migration name choice

The migration is `AddOrderPaymentMethod` (singular), NOT `AddOrderPaymentMethodAndPaidAt` as the INDEX row originally said. Reason: `paid_at` already exists in `Migrations/20260603110319_Orders.cs:36` (T-0060 shipped it as a nullable timestamp). Recording that in the ticket name is more honest than a misleading composite name.

### Why no index on `payment_method`

At MVP order volume (~tens per day per country) the `GROUP BY payment_method` scan is sub-millisecond. Adding an index for ad-hoc admin queries is premature. If volume grows or admin analytics become hot, a partial index `WHERE is_active` is the right shape — but that's a future ticket.

## Test plan

Inline above (see Scope > Tests).

## Status log

- 2026-06-06 `draft → ready` by PM. Expanded from INDEX row after T-0066 merged. Four user decisions captured upfront via a 4-reader research workflow + synthesis judge:
  - **Q1** — widen `WebhookPayload` to surface `PaymentStatus.PaidAt`; trust Comgate's PAID timestamp. Better data quality for the invoice; future SK/PL/HU providers get the plumbing for free.
  - **Q2** — T-0067 does NOT enqueue `invoice.generate`. T-0068 owns the third `outbox.Enqueue` + dispatcher routing. Cleanest separation; no half-wired event types in master.
  - **Q3** — Per-event-type switch in `EmailSendService.SendAsync`. Strict typing; adding a new event = one new case.
  - **Q4** — Pre-bake the action URL into the outbox payload at enqueue time. Producer owns URL construction; consumer is a stateless drainer.

  Verified upfront: `paid_at` column already exists in T-0060's migration — only `payment_method` is genuinely new (the migration name reflects that). `EmailSendService.SendAsync` currently hardcodes `JsonSerializer.Deserialize<OneTimeTokenOutboxPayload>` — Q3 generalisation is required. No order email templates exist on master; T-0067 ships the seed migration. `IOutbox.Enqueue(aggregateId, eventType, payloadJson)` is the API; PascalCase JSON property names match existing `OneTimeTokenOutboxPayload` precedent.
