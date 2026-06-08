---
id: T-0071
title: AcceptOrder command (maker action) + customer-notification outbox event
status: ready
size: S
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0060, T-0011]
blocks: [T-0072, T-0073]
user_stories: [US-maker-0006]
adrs: [0013, 0014, 0019, 0020]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, infra-database, web-maker, frontend-i18n]
---

# T-0071 — AcceptOrder command (maker action) + customer-notification outbox event

## Context

First maker-initiated state transition in the order lifecycle. After T-0067's `MarkOrderPaid.Handler` parks an order in `OrderState.Paid` and notifies both customer + maker by email, the maker reviews the spec sheet (attachments fetched via T-0064 `DownloadAttachment`) and clicks **Accept** on `/dashboard/maker/objednavka/{id}` (T-0087 wires the button). T-0071 ships the backend that the button calls: `POST /api/v1/maker/orders/{orderId}/accept`. The handler verifies the maker owns the order (IDOR shield per ADR 0013), calls `order.Accept(IClock)` (already present on the entity from T-0060), and enqueues a single `order.accepted.customerEmail` outbox row so the customer learns the maker has committed to fulfilling.

The slice is the **state transition + customer-notification half** of the maker-acceptance flow. The `Order.Accept(IClock)` aggregate method already exists from T-0060 and flips `State` to `OrderState.Accepted` + sets `AcceptedAt`. T-0071 wires the use-case file, the controller action, the outbox event type, the payload record, the `EmailTemplateType` enum value + seed migration, and the `IEmailSendService` per-event-type switch branch. **No new domain logic** (the entity ships nothing new — `Accept` and `AcceptedAt` already exist). **No shipping-side effects** (T-0072 / T-0073 own the Accepted → Shipped path). **No decline counter-action** (decision A.1 below).

T-0071 sits inside the **shipping-pipeline bundle** (T-0070 + T-0071 + T-0072 + T-0073 + T-0074 + T-0075). All six tickets ship in one PR; the implementer processes them sequentially in the same branch. T-0071 unblocks T-0072 (Zásilkovna ShipOrder) and T-0073 (personal-pickup ShipOrder) — both transitions require an order in `OrderState.Accepted`, which only this ticket can produce. Within the bundle, the ordering is T-0070 (carrier seam) → T-0071 (accept) → T-0072 / T-0073 (ship) → T-0074 (label) → T-0075 (download).

The integration is **NOT security-touching** — no new key-vault secrets, no new public endpoint, no new external HTTP integration. The maker host already enforces audience-bound JWT + `[Authorize]` (a customer JWT cannot replay against `/api/v1/maker/*`), and the IDOR shield is the established `GetByIdForMakerAsync(orderId, makerId)` pattern from T-0064 + the existing `Web.Maker.OrdersController` precedent. The action follows the proven `MarkOrderPaid` blueprint (T-0067) almost line-for-line: load → guard → transition → resolve language → build payload → enqueue → return; UoW pipeline behavior commits the entity mutation + outbox row atomically per ADR 0014.

ADR 0019 (SendGrid email pipeline) + ADR 0020 (outbox + retry policy) + ADR 0014 (UoW) + ADR 0013 (scoped repositories) cover the architectural shape end to end. No new ADR is required — every choice falls inside the established precedent. The user pre-locked two scope-shape decisions before this ticket transitioned to ready (no Decline counter-action; no auto-cancel deadline); the remaining choices are PM-absorbed because they follow T-0067 exactly.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user answered 2 blocking AskUserQuestion items at `/feature` step 3 before this ticket transitioned to ready (decline counter-action; auto-cancel deadline). The remaining choices follow T-0067 precedent exactly and are PM-absorbed.

### A. User-locked at /feature step 3 (non-negotiable)

1. **No DeclineOrder counter-action in T-0071.** Maker cannot decline a Paid order via this command. If a maker cannot fulfil, they escalate to admin (T-0107 ChangeOrderStateManually). Smaller scope; cleaner state graph. **Rejected:** in-bundle Decline (~6 extra files, doubles T-0071 size); defer to separate T-008X ticket (kicks the decision down the road without solving it).

2. **No auto-cancel deadline.** Once Paid, order is the maker's responsibility per maker SLA. Dashboard nudges (T-0087) + admin monitoring (T-0118) flag stale Paid orders. Avoids surprising auto-cancellations during maker vacations. **Rejected:** 24h auto-cancel (refund storms during holidays); configurable via CountryConfiguration (speculative; adds Function infrastructure).

### B. ADR-locked (no relitigation)

- **One-file feature shape per patterns §A.13.** `Features/Orders/AcceptOrder.cs` contains nested `Command`, `Response`, `Validator`, `Handler`. Mirrors `MarkOrderPaid.cs`.
- **UoW pipeline commits per ADR 0014.** The order mutation (`State → Accepted`, `AcceptedAt`) AND the outbox row ship in one Postgres transaction via `UnitOfWorkPipelineBehavior`. **No `SaveChangesAsync()` in the handler.**
- **Scoped repositories per ADR 0013.** Ownership IDOR shield via `IOrderRepository.GetByIdForMakerAsync(orderId, makerId, ct)` (already exists from T-0064). Returns `null` for cross-maker / unknown ids — same IDOR-resistant 404 shape.
- **Outbox event naming per T-0067 convention.** `<domain>.<action>.<modality>` → `order.accepted.customerEmail` ("the order domain says accepted; the customer email modality should fire"). New constant added to `OutboxEventTypes`; `IsEmailSend` extended.
- **Per-event-type switch in `EmailSendService.SendAsync` per T-0067 Q3.** New 4th switch arm for `OrderAcceptedCustomerEmail`. Strict typing; the discriminated routing is a compile-time check.
- **Action URL pre-baking per T-0067 Q4.** Producer (`AcceptOrder.Handler`) pre-bakes `{WebBaseUrl}/objednavka/{order.Id}` into the payload at enqueue time. Consumer (`EmailSendService`) passes it verbatim to SendGrid.
- **Language resolved at enqueue time per T-0028 + T-0067 pattern.** `ILanguageResolver.ResolveForUserAsync(customer, ct)` captures the customer's language at-time-of-accept. Frozen into the payload.
- **TDD with commit-order discipline per T-0067 hard rule.** Domain + handler tests committed BEFORE the implementation in the same branch. Entity tests for `Accept` already exist from T-0060 — extend, not duplicate.

### C. PM-absorbed (no user input needed)

- **Endpoint:** `POST /api/v1/maker/orders/{orderId}/accept`. Mirrors T-0072 `POST /api/v1/maker/orders/{orderId}/ship` precedent + RESTful action-resource convention.
- **Outbox event name:** `order.accepted.customerEmail` per T-0067 `<domain>.<action>.<modality>` convention.
- **Email template:** `EmailTemplateType.OrderAcceptedCustomer` with cs-CZ + en-US translations seeded via new EF migration.
- **Payload ActionUrl:** Pre-baked to `{WebBaseUrl}/objednavka/{orderId}` per T-0067 Q4 precedent.
- **IDOR ownership check:** Use `IOrderRepository.GetByIdForMakerAsync(orderId, makerId)` (already shipped at T-0060/T-0064). Returns `Error.NotFound` on miss per ADR 0013 (IDOR-leak-resistant 404).
- **Side-effects:** Email only. No invoice generation (already done at T-0068b). No payout pre-allocation (T-0102).
- **Email body contents:** order summary (number, total, contact name, action URL); fits in T-0067-shape `OrderPaidCustomerEmailPayload` template (same fields).

## Scope

### Domain layer

- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — add a fourth email-modality constant + extend the `IsEmailSend` classifier:
  ```csharp
  public const string OrderAcceptedCustomerEmail = "order.accepted.customerEmail";

  public static bool IsEmailSend(string eventType) =>
      eventType is AuthMagicLinkSend
                or AuthEmailConfirmationSend
                or AuthPasswordResetSend
                or OrderPaidCustomerEmail
                or OrderPlacedMakerEmail
                or OrderAcceptedCustomerEmail;
  ```
  Do NOT touch `IsInvoiceGenerate` — it stays disjoint.

- **`Core.Domain/Outbox/OrderAcceptedCustomerEmailPayload.cs`** (NEW) — sealed record with PascalCase JSON property names matching `OrderPaidCustomerEmailPayload`. Mirror its XML doc layout (language captured at enqueue; ActionUrl pre-baked). Shape:
  ```csharp
  public sealed record OrderAcceptedCustomerEmailPayload(
      string OrderId,
      string OrderNumber,
      string Email,
      string ContactName,
      string LanguageCode,
      string ActionUrl);   // {WebBaseUrl}/objednavka/{OrderId}
  ```
  Note: NO `TotalAmountMinor` / `Currency` here. The customer already received those values in the T-0067 "thanks for your order" email; the acceptance email is shorter: "Your maker accepted the order — link to view status." Keeps the template diff minimal vs `OrderPaidCustomer` while still distinct enough to merit a separate template.

- **`Core.Domain/Email/EmailTemplateType.cs`** — add value `6`:
  ```csharp
  /// <summary>
  /// "Your maker accepted the order" customer notification. Outbox event:
  /// <see cref="Outbox.OutboxEventTypes.OrderAcceptedCustomerEmail"/>. T-0071.
  /// </summary>
  OrderAcceptedCustomer = 6,
  ```
  Value 6 picks up where T-0067 left off (1–5 already taken).

- **`Core.Domain/Orders/Order.cs`** — **NO CHANGES**. `Order.Accept(IClock)` (lines 527-536) + `AcceptedAt` (line 141) already exist from T-0060. The handler calls them as-is.

- **`Core.Domain/Common/BusinessErrorMessage.cs`** — **NO new codes**. Reuses:
  - `OrderNotFound` — IDOR miss on `GetByIdForMakerAsync`.
  - `OrderInvalidTransition` — `Accept` from non-Paid state.
  - `MakerNotFound` — no maker row for the authenticated user (shouldn't happen on the Maker host but is a defensive 404 for safety).
  - `Unauthorized` shape — no session user id (handled by `[Authorize]` first, but the handler still guards via `IUserSessionProvider.GetUserId()`).

### AppServices layer

- **`Core.AppServices/Features/Orders/AcceptOrder.cs`** (NEW) — one file containing nested `Command` / `Response` / `Validator` / `Handler`. Mirror `MarkOrderPaid.cs` structure verbatim. Final shape:

  ```csharp
  public static class AcceptOrder
  {
      public sealed record Command(string OrderId) : ICommand<Response>;

      public sealed record Response(string OrderId);

      public sealed class Validator : AbstractValidator<Command>
      {
          public Validator()
          {
              RuleFor(c => c.OrderId)
                  .Cascade(CascadeMode.Stop)
                  .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                  .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
          }
      }

      public sealed class Handler(
          IOrderRepository orders,
          IUserRepository users,
          IMakerRepository makers,
          IUserSessionProvider session,
          IOutbox outbox,
          IClock clock,
          ILanguageResolver languageResolver,
          IOptions<PublicAppUrlsOptions> publicAppUrls,
          ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
      {
          public async Task<BusinessResult<Response>> Handle(
              Command command, CancellationToken cancellationToken)
          {
              // Step 1: Resolve session.
              var userId = session.GetUserId();
              if (string.IsNullOrEmpty(userId))
              {
                  return BusinessResult.Failure<Response>(Error.Unauthorized());
              }

              // Step 2: Resolve maker for the authenticated user.
              var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
              if (maker is null)
              {
                  // User has a maker-audience token but no maker row.
                  // Defensive: shouldn't happen post-onboarding, but a 404
                  // is the right IDOR-resistant shape.
                  return BusinessResult.Failure<Response>(
                      Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
              }

              // Step 3: Owner-scoped load (IDOR shield per ADR 0013).
              // Cross-maker / unknown ids return null → same 404 shape so
              // order ids aren't enumerable across makers.
              var order = await orders.GetByIdForMakerAsync(
                  command.OrderId, maker.Id, cancellationToken);
              if (order is null)
              {
                  return BusinessResult.Failure<Response>(
                      Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
              }

              // Step 4: State transition via the aggregate. Order.Accept
              // enforces Paid → Accepted; a re-click after success surfaces
              // as OrderInvalidTransition.
              var transitionResult = order.Accept(clock);
              if (!transitionResult.IsSuccess)
              {
                  return BusinessResult.Failure<Response>(transitionResult.Error!);
              }

              // Step 5: Resolve customer + language + build payload.
              // Language frozen at enqueue time per T-0028 / T-0067 pattern.
              var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
              if (customer is null)
              {
                  logger.LogCritical(
                      "AcceptOrder: customer user {UserId} not found for order {OrderId}. " +
                      "FK invariant violation — refusing to commit.",
                      order.CustomerUserId, order.Id);
                  return BusinessResult.Failure<Response>(
                      Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
              }
              var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
              var urls = publicAppUrls.Value;
              var payload = new OrderAcceptedCustomerEmailPayload(
                  OrderId: order.Id,
                  OrderNumber: order.OrderNumber,
                  Email: order.ContactEmail,
                  ContactName: order.ContactName,
                  LanguageCode: customerLanguage,
                  ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
              outbox.Enqueue(
                  aggregateId: order.Id,
                  eventType: OutboxEventTypes.OrderAcceptedCustomerEmail,
                  payloadJson: JsonSerializer.Serialize(payload));

              // Step 6: NO SaveChangesAsync — UoW pipeline behavior commits
              // the order mutation AND the outbox row atomically per ADR 0014.
              return BusinessResult.Success(new Response(order.Id));
          }
      }
  }
  ```

  Note: the handler uses the **same DI surface as `MarkOrderPaid.Handler`** plus `IUserSessionProvider` + `IMakerRepository.GetByUserIdAsync` (since this is a session-driven call, not a webhook-driven one). No new abstractions.

- **`Core.AppServices/Features/Email/IEmailSendService.cs`** — extend the per-event-type switch with a 4th branch for the order-accepted modality. Add a new private helper `SendOrderAcceptedCustomerEmailAsync` modelled on `SendOrderPlacedMakerEmailAsync` (the closest precedent — no PDF attachment, no invoice lookup, just template + substitutions). Concretely:

  Inside `SendAsync`:
  ```csharp
  OutboxEventTypes.OrderAcceptedCustomerEmail
      => SendOrderAcceptedCustomerEmailAsync(payloadJson, cancellationToken),
  ```

  Add the helper:
  ```csharp
  private Task<BusinessResult<EmailSentReceipt>> SendOrderAcceptedCustomerEmailAsync(
      string payloadJson, CancellationToken cancellationToken)
  {
      var payloadResult = DeserializeOrderPayload<OrderAcceptedCustomerEmailPayload>(
          payloadJson, OutboxEventTypes.OrderAcceptedCustomerEmail);
      if (!payloadResult.IsSuccess)
      {
          return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
      }
      var payload = payloadResult.Value!;

      return DispatchOrderEmailAsync(
          templateType: EmailTemplateType.OrderAcceptedCustomer,
          toAddress: payload.Email,
          toName: payload.ContactName,
          languageCode: payload.LanguageCode,
          substitutions: new Dictionary<string, object>
          {
              ["action_url"] = payload.ActionUrl,
              ["order_id"] = payload.OrderId,
              ["order_number"] = payload.OrderNumber,
              ["contact_name"] = payload.ContactName,
              ["language_code"] = payload.LanguageCode,
          },
          // No PDF attachment — that ships only with the OrderPaidCustomer
          // email per T-0069 locked decision 10. Maker-acceptance emails
          // carry no attachment.
          attachment: null,
          cancellationToken: cancellationToken);
  }
  ```

  Reuses the shared `DispatchOrderEmailAsync` + `DeserializeOrderPayload<T>` helpers. No duplication.

### Database layer

- **EF migration `SeedOrderAcceptedEmailTemplate`** — mirrors `Migrations/20260606155359_SeedOrderEmailTemplates.cs` structure (one template row + two translation rows: cs-CZ + en-US). Use `d-placeholder-order-accepted-customer` as the SendGrid Dynamic Template id; the real id lands as a deploy-time Azure App Service configuration override per the established T-0067 pattern.

  Suggested wording (PM/UX may refine):
  - **cs-CZ subject:** `'Vaše objednávka #{{order_number}} byla přijata'`
  - **cs-CZ body:**
    ```
    Dobrý den {{contact_name}},

    váš výrobce přijal objednávku {{order_number}} a začíná na ní pracovat.
    Aktuální stav najdete na: {{action_url}}

    Makables — makables.cz
    ```
  - **en-US subject:** `'Your order #{{order_number}} has been accepted'`
  - **en-US body:**
    ```
    Hi {{contact_name}},

    your maker has accepted order {{order_number}} and is starting work.
    Track its status at: {{action_url}}

    Makables — makables.cz
    ```

  Use the SQL pattern from `20260606155359_SeedOrderEmailTemplates.cs` verbatim (`migrationBuilder.Sql` block + `QuoteSql` helper for body escaping + `seededAtSql` timestamp). Insert one row into `email_templates` (id `'tpl-order-accepted-customer'`) + two rows into `email_template_translations` (ids `'tpl-tr-order-accepted-customer-cs'`, `'tpl-tr-order-accepted-customer-en'`).

  `Down` deletes the three rows by id. Generate via:
  ```
  dotnet ef migrations add SeedOrderAcceptedEmailTemplate \
      --project backend/src/Makables.Infra.Database \
      --startup-project backend/src/Makables.Web.Customer
  ```
  (Any of the four hosts works as the startup project — the `Web.Customer` host is the established convention from prior order migrations.)

- **No schema changes.** No new columns; `AcceptedAt` already exists from T-0060.

### Web.Maker host

- **`Web.Maker/Controllers/OrdersController.cs`** — **EXTEND** the existing controller (do not create a new one). The controller already exists with `DownloadAttachment`; add a second action below it. Add `IMediator mediator` to the primary-constructor DI list (the existing T-0064 action doesn't dispatch via Mediator; this is the first one that does).

  ```csharp
  /// <summary>
  /// Maker accepts a Paid order. Transitions to Accepted and enqueues
  /// the customer-notification outbox event. Mirrors the established
  /// Mediator dispatch pattern from the Customer host's OrdersController.
  /// T-0071 (US-maker-0006).
  /// </summary>
  [HttpPost("{orderId}/accept")]
  [ProducesResponseType(typeof(AcceptOrder.Response), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
  [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
  [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
  [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
  public async Task<IActionResult> Accept(string orderId, CancellationToken ct)
  {
      var result = await mediator.Send(new AcceptOrder.Command(orderId), ct);
      return HandleResult(result);
  }
  ```

  The action is a one-liner per patterns §A.16 (controllers stay thin; Mediator carries the load). `HandleResult` from `MakablesApiController` already maps `Error.NotFound` → 404, `Error.Conflict` → 409, etc.

- The route is registered as `POST /api/v1/orders/{orderId}/accept` (the controller's `[Route("api/v{version:apiVersion}/orders")]` does NOT include the `maker/` segment in the existing T-0064 setup — the **host** is `Web.Maker`, the audience boundary is the JWT audience policy, not the URL path). The decision spec calls this `POST /api/v1/maker/orders/{orderId}/accept`; resolve to the existing controller's route prefix. **Implementer judgement:** match the existing controller's `[Route]` prefix verbatim so the URL is `POST /api/v1/orders/{orderId}/accept` on the Maker host. The "maker/" semantic is enforced by audience, not URL segment, consistent with the T-0064 precedent at `Web.Maker.OrdersController:30`.

- **`Web.Maker/Program.cs`** — verify Mediator + AppServices module is already registered (it is — `MarkOrderPaid` runs on the Customer + Public hosts, and the Maker host already includes `Core.AppServices`). No new `AddMakablesXxx()` call needed.

### NSwag regen

The new `POST /api/v1/orders/{orderId}/accept` endpoint is a public API contract change for the Maker host → **NSwag regen REQUIRED in the same PR**. Per pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff. Expected diff: new `acceptOrder` client method + `AcceptOrder_Response` DTO.

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — **NO new keys**. The handler returns error codes that already have translations:
  - `OrderNotFound` (`'order.notFound'`) — translated from T-0060.
  - `OrderInvalidTransition` (`'order.invalidTransition'`) — translated from T-0060.
  - `OrderCustomerUserMissing` (`'order.customerUserMissing'`) — translated from T-0067.
  - `Required` / `MaxLength` (validator codes) — translated since the auth tickets.
  - `Unauthorized` shape — `[Authorize]` filter handles before the action runs.

  The email subject + body wording lives in the `email_template_translations` table (DB-backed) per T-0028's pattern, NOT in `cs-CZ.ts`.

- **No frontend success-toast key needed.** T-0087 (`/dashboard/maker/objednavka/[id]`) is the frontend caller and owns its own confirmation strings; T-0071 ships only the API + error-code surface.

### Tests

Per the TDD-with-commit-order hard rule (T-0067+), domain-level + pure-logic tests are committed BEFORE the implementation in the same branch. Integration tests come after.

#### Unit — `Makables.Tests/`

- **`AppServices/Features/Orders/AcceptOrderHandlerTests.cs`** (NEW, ~7 tests):
  - `Handler_with_unauthenticated_session_returns_Unauthorized` — `IUserSessionProvider.GetUserId()` returns null/empty.
  - `Handler_with_no_maker_for_user_returns_OrderNotFound` — `IMakerRepository.GetByUserIdAsync` returns null. Pin the IDOR-resistant 404 shape.
  - `Handler_with_order_owned_by_DIFFERENT_maker_returns_OrderNotFound` — `GetByIdForMakerAsync(orderId, makerId)` returns null (cross-maker IDOR). Verify outbox.Enqueue is NEVER called.
  - `Handler_with_order_not_in_Paid_state_returns_OrderInvalidTransition` — seed the order in `OrderState.Pending Payment` or `Accepted`; `order.Accept(clock)` refuses. Verify outbox.Enqueue is NEVER called (transition failed before the enqueue step).
  - `Handler_happy_path_transitions_order_to_Accepted_and_sets_AcceptedAt` — seed in `Paid`; assert `order.State == Accepted` + `order.AcceptedAt == clock.UtcNow` after handler returns Success.
  - `Handler_happy_path_enqueues_one_OrderAcceptedCustomerEmail_outbox_row` — NSubstitute verify on `IOutbox.Enqueue(order.Id, "order.accepted.customerEmail", <json>)` called exactly once. Deserialize the captured JSON; assert every payload field (OrderId, OrderNumber, Email, ContactName, LanguageCode, ActionUrl) matches the seeded order + resolved language + `{WebBaseUrl}/objednavka/{order.Id}`.
  - `Handler_with_missing_customer_user_returns_OrderCustomerUserMissing_and_does_not_enqueue` — `IUserRepository.GetByIdAsync(customerUserId)` returns null (FK invariant). Verify `outbox.Enqueue` is NEVER called. Pin the FK-violation shape.

- **`AppServices/Features/Email/EmailSendServiceTests.cs`** — extend (~3 new tests):
  - `SendAsync_with_OrderAcceptedCustomerEmail_routes_to_OrderAccepted_branch_and_calls_provider` — happy path; NSubstitute verify on `IEmailProvider.SendAsync` with `templateType == OrderAcceptedCustomer`.
  - `SendAsync_with_OrderAcceptedCustomerEmail_passes_substitutions_with_action_url_and_order_number_and_contact_name` — verify the `Dictionary<string, object>` keys match the spec (`action_url`, `order_id`, `order_number`, `contact_name`, `language_code`). No `total_amount` / `currency` keys (those are NOT in this payload).
  - `SendAsync_with_malformed_OrderAcceptedCustomerEmail_payload_returns_OrderEmailPayloadMalformed_Permanent` — pass garbage JSON; assert `Error.Permanent(OrderEmailPayloadMalformed)`. Pin the discriminated-failure shape.

- **`Core.Domain/Outbox/OutboxEventTypesTests.cs`** (if it exists — otherwise add) — 1 new test: `IsEmailSend_returns_true_for_OrderAcceptedCustomerEmail`. If no such file exists, add this assertion inline to the existing `IsEmailSend` test cluster.

#### Integration — `Makables.IntegrationTests/`

- **`Orders/AcceptOrderIntegrationTests.cs`** (NEW, ~2 tests, end-to-end against Postgres + IOutbox):
  - `POST_accept_happy_path_transitions_order_and_enqueues_one_outbox_row` — seed a Paid order owned by the test maker; POST `/api/v1/orders/{orderId}/accept` with a maker JWT; assert 200; reload order from DB: `State == Accepted`, `AcceptedAt` populated; query `outbox_events` for `aggregate_id == order.Id`; assert exactly 1 row with `event_type == 'order.accepted.customerEmail'`; deserialize payload and assert ActionUrl shape + Email + LanguageCode.
  - `POST_accept_for_DIFFERENT_makers_order_returns_404_without_enqueue` — seed a Paid order owned by a DIFFERENT maker; POST with the test maker's JWT; assert 404; assert 0 outbox rows for that order.

#### Test counts

Baseline post-T-0070 (within the bundle) is captured by T-0070's AC-14. T-0071 adds:
- Unit: ~11 new tests (7 handler + 3 email-service branch + 1 IsEmailSend assertion).
- Integration: ~2 new tests.

### Docs

- **`docs/architecture/roles/order.md`** — update the lifecycle table: `Paid → Accepted` row notes the new `AcceptOrder.Command` (T-0071) as the producer + the `order.accepted.customerEmail` outbox event as the side effect.
- **`docs/tickets/INDEX.md`** — flip T-0071 row to `**done**` after PR merge (PM does this).

## Alternatives Considered

- **Option A — Include a DeclineOrder counter-action in T-0071.** *Rejected per A.1* — doubles the ticket size (~6 extra files: counter-command, validator, handler, new `Order.Decline` method, error codes, tests). The maker SLA path is "escalate to admin" (T-0107 `ChangeOrderStateManually`), not "decline at will" — declining mid-payment requires a Comgate refund which T-0105 owns. Cleaner state graph; smaller blast radius.
- **Option B — Defer Decline to a follow-up T-008X ticket.** *Rejected per A.1* — kicks the decision down the road without solving it. The admin-escalation path (T-0107) is the long-term answer; opening a placeholder ticket pretends otherwise. If a real Decline workflow emerges (e.g., maker vacation mode), it gets its own deliberation round at that time.
- **Option C — 24h auto-cancel deadline for stale Paid orders.** *Rejected per A.2* — refund storms during holidays (a maker on a 2-week vacation comes back to dozens of auto-cancelled orders + auto-refund-triggered Comgate fees + customer complaints). The maker SLA is contract-driven, not enforced by a timer.
- **Option D — CountryConfiguration-driven configurable auto-cancel window.** *Rejected per A.2* — speculative. Adds Function infrastructure (a timer-triggered `StalePaidOrderSweep`) for a feature nobody asked for. T-0087 dashboard nudges + T-0118 admin monitoring catch stale orders at a fraction of the complexity cost.
- **Option E — Reuse `OrderPaidCustomerEmailPayload` (no new payload record).** *Rejected per PM-absorbed §payload shape* — the OrderPaid payload carries `TotalAmountMinor` + `Currency` (invoice-attached email). The Accept email is shorter ("your maker accepted; here's the link") and carries no PDF. A distinct record keeps the contract honest + the template substitution dictionary minimal. The cost is one record file + one switch arm — trivial.
- **Option F — Webhook-style command with no session resolution.** *Rejected* — `AcceptOrder` is a session-driven maker action (the UI button in T-0087), not a webhook. The handler must resolve `IUserSessionProvider.GetUserId()` to know which maker is acting. Mirrors the Customer-host `AddOrderAttachment.Handler` shape, not the `MarkOrderPaid.Handler` (which trusts the webhook controller's vetting).
- **Option G — Path segment `maker/` in the route.** *Rejected per controller-precedent* — the existing `Web.Maker.OrdersController` uses `[Route("api/v{version:apiVersion}/orders")]`. The "maker" semantic is enforced by JWT audience policy in `AddMakablesAuth`, not URL segment. The decision spec's `/api/v1/maker/orders/...` shorthand resolves to `/api/v1/orders/{orderId}/accept` on the Maker host — same audience boundary, simpler controller.

## Out of scope

- **Maker DeclineOrder counter-action** — per A.1. If a real workflow emerges, opens as a separate deliberation round.
- **Auto-cancel of stale Paid orders** — per A.2.
- **Accepted → Shipped transition** — T-0072 (Zásilkovna path) + T-0073 (personal-pickup path).
- **Customer-facing "your order was accepted" UI surface** — T-0087 frontend ticket.
- **Maker dashboard Accept-button wiring** — T-0087.
- **Maker in-app notification (vs email)** — post-MVP feature; T-0071 emits ONLY email.
- **Real SendGrid template provisioning** — `d-placeholder-order-accepted-customer` ships in the seed migration; the real id lands as a deploy-time Azure App Service configuration override per the T-0067 pattern.
- **Payout pre-allocation on Accept** — T-0102 owns payout-state plumbing. Accept does NOT touch ledgers.
- **Admin manual state-change to Accepted** — T-0107 `ChangeOrderStateManually` (separate command; unscoped repository; bypasses maker session).
- **Refund email parallel for declined orders** — T-0105 territory.

## Acceptance criteria

- **AC-1** Given the codebase, when it builds, then `Core.AppServices/Features/Orders/AcceptOrder.cs` exists with nested `Command(string OrderId)`, `Response(string OrderId)`, `Validator`, and `Handler` per the one-file feature shape (patterns §A.13).
- **AC-2** Given a `POST /api/v1/orders/{orderId}/accept` with a valid maker JWT and a seeded order in `OrderState.Paid` owned by the authenticated maker, when the handler runs, then `order.State` transitions to `OrderState.Accepted` AND `order.AcceptedAt == clock.UtcNow` (verified in DB after UoW commit).
- **AC-3** Given the same happy path, when the handler returns, then exactly **one** outbox row exists for `aggregate_id == order.Id` with `event_type == 'order.accepted.customerEmail'`. The payload JSON deserializes to `OrderAcceptedCustomerEmailPayload` with: `OrderId == order.Id`, `OrderNumber == order.OrderNumber`, `Email == order.ContactEmail`, `ContactName == order.ContactName`, `LanguageCode == <resolved customer language>`, `ActionUrl == "{WebBaseUrl}/objednavka/{order.Id}"` (asserted exact string).
- **AC-4** Given a maker JWT and an `orderId` that resolves to an order owned by a DIFFERENT maker, when the handler runs, then it returns `Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound)` (IDOR-resistant 404, same shape as unknown-id miss) AND `IOutbox.Enqueue` is never called.
- **AC-5** Given a maker JWT and an order in any state OTHER than `OrderState.Paid` (e.g., `PendingPayment`, `Accepted`, `Shipped`, `Cancelled`), when the handler runs, then it returns `BusinessResult.Failure(Error.* with code OrderInvalidTransition)` AND `IOutbox.Enqueue` is never called AND `order.State` is unchanged.
- **AC-6** Given a maker JWT and an order where `IUserRepository.GetByIdAsync(order.CustomerUserId)` returns null (FK invariant violation), when the handler runs, then it returns `BusinessResult.Failure(Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing))`, logs Critical, AND the order is NOT transitioned (refuse to commit so Comgate-replay-style retries / re-clicks don't progress half-state).
- **AC-7** Given a request with no authenticated session (no Bearer token), when the request reaches the Maker host, then `[Authorize]` returns `401 Unauthorized` BEFORE the handler runs. If the session somehow has no user id mid-flight, the handler's defensive guard returns `Error.Unauthorized()`.
- **AC-8** Given the EF migration `SeedOrderAcceptedEmailTemplate` is applied to an empty postgres:16-alpine container, when `MigrateAsync()` completes, then:
  - One row exists in `email_templates` with `id == 'tpl-order-accepted-customer'`, `type == 'OrderAcceptedCustomer'`, `provider_template_id == 'd-placeholder-order-accepted-customer'`, `is_active == TRUE`, `country_code == 'CZ'`.
  - Two rows exist in `email_template_translations` with ids `'tpl-tr-order-accepted-customer-cs'` (language_code `'cs-CZ'`) and `'tpl-tr-order-accepted-customer-en'` (language_code `'en-US'`), both linked to `email_template_id == 'tpl-order-accepted-customer'`.
  - `Down` reverses cleanly (deletes the three rows by id).
- **AC-9** Given `OutboxEventTypes.IsEmailSend("order.accepted.customerEmail")`, when called, then it returns `true`. The other 5 modalities (auth × 3, OrderPaidCustomer, OrderPlacedMaker) continue to return `true`; `IsEmailSend("invoice.generate")` continues to return `false` (no regression).
- **AC-10** Given `IEmailSendService.SendAsync("order.accepted.customerEmail", <valid payload JSON>, ct)`, when called, then it routes to the new `SendOrderAcceptedCustomerEmailAsync` branch → resolves `EmailTemplateType.OrderAcceptedCustomer` via `IEmailTemplateRepository` → resolves the translation in the payload's language code (with `LanguageCode.DefaultFallback` fallback) → calls `IEmailProvider.SendAsync` with substitutions including `action_url`, `order_id`, `order_number`, `contact_name`, `language_code` (no `total_amount` / `currency` / no attachment). Returns `BusinessResult.Success<EmailSentReceipt>`.
- **AC-11** Given `IEmailSendService.SendAsync` with a malformed `order.accepted.customerEmail` payload (garbage JSON or missing required fields), when called, then it returns `BusinessResult.Failure(Error.Permanent(OrderEmailPayloadMalformed))` — the existing T-0067 discriminated failure code, no new code added.
- **AC-12** Build clean (zero warnings; no `Console.*`; no `dynamic`; no `SaveChangesAsync()` in the handler — UoW commits atomically per ADR 0014). Frontend ESLint clean (no new keys to add).
- **AC-13** Test counts: unit tests +~11 (AcceptOrderHandlerTests ~7 + EmailSendServiceTests +3 + OutboxEventTypes +1). Integration tests +2 (`AcceptOrderIntegrationTests`). All new + extended tests pass.
- **AC-14** Consistency script (`tools/consistency-check.ps1` or repo equivalent) exit 0 (no new T1–T7 violations vs the post-T-0070 baseline).
- **AC-15** NSwag regen committed in the same PR: `frontend/src/lib/api-client/` has the new `acceptOrder` method + `AcceptOrder_Response` DTO. `npm run generate:api` produces a clean diff matching the new endpoint only.

## Technical notes

### Why no new domain method on Order

`Order.Accept(IClock)` already exists on the entity (T-0060, `Order.cs:527-536`) along with `AcceptedAt` (`Order.cs:141`). The state transition is `OrderState.Paid → OrderState.Accepted` with `AcceptedAt = clock.UtcNow`; the pre-condition guard is `if (State != OrderState.Paid) return InvalidTransition();`. T-0060 anticipated T-0071 and shipped the method ahead of the caller. T-0071 is a pure wiring ticket on the entity side — no new method, no new field, no schema change.

### Why a distinct payload record (not reusing OrderPaidCustomerEmailPayload)

The OrderPaid payload was designed around the "thanks + invoice attached" email and carries `TotalAmountMinor` + `Currency` for the invoice line item. The Accept email is shorter: "Your maker accepted the order. Here's the link to track it." Carrying unused fields invites the consumer to depend on them; a distinct record keeps the substitution dictionary lean and makes the email template's substitution-list audit-friendly. The cost is one ~12-line record + one switch arm — well below the threshold where DRY pressure would justify reuse.

### Why no PDF attachment on the Accept email

T-0069 locked decision 10 already established the rule: the invoice PDF rides ONLY on the `OrderPaidCustomer` email (the "thanks" email is the customer's natural touchpoint for the invoice). Subsequent state-change emails (Accepted, Shipped, Delivered, Completed) do NOT re-attach the PDF — the customer has it from the prior email. The Accept email passes `attachment: null` to `DispatchOrderEmailAsync` explicitly, mirroring the `OrderPlacedMaker` precedent.

### Why the action URL points to /objednavka/{id} (customer surface), not /dashboard/maker/...

The recipient is the **customer**. They want a "view my order status" link, not a maker-dashboard link. The customer-facing surface lives at `/objednavka/{id}` (already established by T-0067's `OrderPaidCustomerEmailPayload.ActionUrl`). Reusing the convention means the customer's email-history maps cleanly to a single landing surface across the order lifecycle ("paid" email → same URL; "accepted" email → same URL; "shipped" email later → same URL with new state visible).

### Why IUserSessionProvider, not webhook-style trust

`MarkOrderPaid` runs on the Public host driven by a Comgate webhook — the webhook controller does signature + IP allowlist vetting upfront so the handler can trust the inputs. `AcceptOrder` runs on the Maker host driven by an authenticated UI click — the handler MUST resolve the actor from `IUserSessionProvider.GetUserId()` because the URL's `{orderId}` segment is user-controlled and cross-maker spoof is the obvious attack. The IDOR shield is `IMakerRepository.GetByUserIdAsync(userId)` + `IOrderRepository.GetByIdForMakerAsync(orderId, maker.Id)` — both already shipped, both return `null` on miss for IDOR-resistant 404. Pattern mirrors `Web.Customer.OrdersController.AddOrderAttachment`.

### Why one outbox row (not two)

The maker who just clicked Accept does NOT need an email back ("you accepted the order you just clicked Accept on") — the UI confirmation toast in T-0087 is the right channel. T-0067 enqueued 2 emails (customer-thanks + maker-new-order) because the customer paid via a redirect flow (no UI to confirm) and the maker was offline (didn't witness the event). For Accept, the maker is online + has UI feedback; only the customer is offline + needs notification. The atomic-with-UoW commit guarantees the email queues iff the transition commits, per ADR 0014.

### Why no auto-cancel timer (decision A.2 deep dive)

A maker-vacation auto-cancel would have to handle:
- Refund-storm: every auto-cancel triggers a Comgate refund (T-0105) which incurs a per-transaction fee.
- Customer-surprise: the customer paid 23h ago and the auto-canceller cancels at hour 24 the moment their maker comes online for breakfast.
- Carve-outs: holidays, weekends, maker time-zone, customer time-zone, sliding window vs fixed window.

The simpler answer is contract: makers agree to an N-day SLA at onboarding (T-0118 admin monitoring flags violators). The dashboard nudge in T-0087 ("you have 3 Paid orders waiting") closes the loop without the timer machinery. If volume reveals a real problem, a follow-up ticket can add the timer with full deliberation.

## Files touched (expected)

### New
- `backend/src/Makables.Core.AppServices/Features/Orders/AcceptOrder.cs`
- `backend/src/Makables.Core.Domain/Outbox/OrderAcceptedCustomerEmailPayload.cs`
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_SeedOrderAcceptedEmailTemplate.cs` (+ Designer + ModelSnapshot regen)
- `backend/src/Makables.Tests/AppServices/Features/Orders/AcceptOrderHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/AcceptOrderIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` — add `OrderAcceptedCustomerEmail` constant + extend `IsEmailSend`.
- `backend/src/Makables.Core.Domain/Email/EmailTemplateType.cs` — add `OrderAcceptedCustomer = 6`.
- `backend/src/Makables.Core.AppServices/Features/Email/IEmailSendService.cs` — add 4th switch arm + `SendOrderAcceptedCustomerEmailAsync` helper.
- `backend/src/Makables.Web.Maker/Controllers/OrdersController.cs` — add `Accept` action + add `IMediator` to primary-ctor DI.
- `backend/src/Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs` — 3 new tests for the new branch.
- `backend/src/Makables.Infra.Database/Migrations/MakablesDbContextModelSnapshot.cs` — auto-regenerated from `dotnet ef migrations add`.
- `frontend/src/lib/api-client/*` — NSwag-regenerated; committed in the same PR.
- `docs/architecture/roles/order.md` — lifecycle table notes the new producer + outbox event.

### Untouched (explicit, to anchor scope)
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — NO changes (Accept + AcceptedAt already exist from T-0060).
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — NO new codes (reuses OrderNotFound, OrderInvalidTransition, OrderCustomerUserMissing).
- `frontend/src/lib/i18n/cs-CZ.ts` — NO new keys (all returned error codes have existing translations).
- `backend/src/Makables.Web.Maker/Program.cs` — NO changes (AppServices module already registered for T-0064 + downstream tickets in the same bundle).

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0071.md`.

## Status log

- 2026-06-08 `draft` by PM. Created from INDEX line (T-0071 row) as part of the shipping-pipeline bundle grooming pass after T-0070 transitioned to ready. The bundle (T-0070 + T-0071 + T-0072 + T-0073 + T-0074 + T-0075) ships in one PR; T-0071 is the second slot.
- 2026-06-08 `draft → ready` by PM. User answered 2 blocking AskUserQuestion items per `/feature` workflow step 3:
  - **A.1** — no Decline counter-action in T-0071; maker escalates to admin (T-0107) when they cannot fulfil. Smaller scope; cleaner state graph.
  - **A.2** — no auto-cancel deadline on Paid orders. Maker SLA is contract-driven; dashboard nudges (T-0087) + admin monitoring (T-0118) flag stale orders. Avoids refund storms during holidays.

  Remaining choices PM-absorbed per T-0067 precedent (one-file feature shape; UoW pipeline commits; outbox event naming convention; per-event-type switch in EmailSendService; action URL pre-baking; language at-enqueue resolution; IDOR shield via GetByIdForMakerAsync; no new BusinessErrorMessage codes; no new i18n keys). Verified upfront: `Order.Accept(IClock)` already exists from T-0060 (`Order.cs:527`); `AcceptedAt` already exists (`Order.cs:141`); `EmailTemplateType` enum has values 1–5 (T-0067), so 6 is next; `OutboxEventTypes` follows `<domain>.<action>.<modality>` convention; `OrderPaidCustomerEmailPayload` is the shape precedent; `Web.Maker.OrdersController` already exists from T-0064 and only needs extending; controller route prefix is `api/v{version}/orders` (no `maker/` segment — audience boundary is JWT, not URL). No `manual_steps` flagged. **Ready for dotnet-backend.**
