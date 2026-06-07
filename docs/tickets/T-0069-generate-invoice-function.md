---
id: T-0069
title: GenerateInvoiceFunction (queue-triggered from outbox) + customer email PDF attachment via lookup-at-send-time
status: ready
size: M
owner: dotnet-backend
created: 2026-06-06
updated: 2026-06-07
depends_on: [T-0029, T-0068b]
blocks: []
user_stories: [US-customer-0017]
adrs: [0019, 0020]
phase: 4
manual_steps: [generate-invoice-queue-config]
security_touching: false
layers: [domain, appservices, infra-clients, infra-functions, config]
---

# T-0069 — GenerateInvoiceFunction (queue-triggered from outbox) + customer email PDF attachment

## Context

Closes the Phase 4 invoice loop. T-0068b shipped `IssueInvoice.Command` (renders + persists + uploads PDF to blob) and added `MarkOrderPaid.Handler`'s third `outbox.Enqueue(InvoiceGenerate, payload)` call. T-0069 wires the queue-triggered Function that dispatches the command + extends the customer order-paid email path to attach the PDF.

The flow end-to-end after T-0069 lands:
1. Comgate webhook → `MarkOrderPaid.Handler` enqueues **3 atomic outbox events** under the same UoW: `order.paid.customerEmail`, `order.placed.makerEmail`, `invoice.generate`.
2. `ProcessOutboxFunction` (T-0029, timer + HTTP) loads due events, classifies by type, publishes to per-event-type Storage Queue.
3. T-0067's email events → existing `send-email` queue → `SendEmailFunction` → `IEmailSendService`.
4. **NEW (T-0069):** `invoice.generate` event → new `generate-invoice` queue → `GenerateInvoiceFunction` → dispatches `IssueInvoice.Command` via MediatR.
5. **NEW (T-0069):** When `IEmailSendService.SendOrderPaidCustomerEmailAsync` runs (in parallel with #4 or shortly after), it now looks up the Invoice by `OrderId`, downloads the PDF from blob, and attaches it to the SendGrid message.
6. **NEW (T-0069):** If the Invoice row doesn't exist yet (race: email queue won the polling sweep), the email handler returns `Transient` failure → outbox retry policy re-delivers after 1m → by then `IssueInvoice` has finished (typical render < 60s) → attachment succeeds.

The central design choice is **lookup-at-send-time + retry-based eventual consistency** (Q1 locked decision). Atomic enqueue from `MarkOrderPaid` stays unchanged; the email handler tolerates the race via existing retry infrastructure.

## Locked design decisions (from `/feature` deliberation)

Captured per `docs/process/deliberation.md`. The user answered 8 blocking AskUserQuestion items before this ticket transitioned to ready. These are non-negotiable for the implementing agent.

### A. Coordination + plumbing

1. **Email × PDF coordination = lookup-at-send-time + retry-based eventual consistency.** `EmailSendService.SendOrderPaidCustomerEmailAsync` fetches the Invoice via `IInvoiceRepository.GetByOrderIdAsync(payload.OrderId)` at send time. Invoice not yet rendered → return `Transient` failure with new `InvoiceNotYetRendered` code → outbox retry policy (1m → 5m → 15m → 1h...) re-delivers; by next poll the render typically completed. **MarkOrderPaid.Handler's 3 atomic enqueues stay unchanged** — no restructure of T-0067. **Rejected alternatives:** GenerateInvoiceFunction enqueuing email after rendering (breaks ADR 0020 thin-wrapper + T-0067's atomic-3-events contract); pre-loading `PdfBlobPath` into `OrderPaidCustomerEmailPayload` (adds patch-existing-outbox-row verb); two separate emails (worse UX).

2. **Dispatcher routing = separate `generate-invoice` queue.** New `OutboxQueues:GenerateInvoiceQueueName` config setting (default `"generate-invoice"`). `OutboxDispatcher.DispatchDueAsync` classifies by event type: `IsEmailSend` events → `PublishSendEmailAsync` (existing); `event_type == OutboxEventTypes.InvoiceGenerate` → new `PublishGenerateInvoiceAsync`. Separate retry budgets + separate poison-message dead-letter + independent scaling. Matches ADR 0020 "one queue per functional cluster". **Rejected:** merged queue (loses failure isolation); inline dispatch from ProcessOutboxFunction (couples render to polling sweep, loses queue retry).

3. **GenerateInvoiceFunction shape = thin MediatR dispatch wrapper.** Mirrors T-0029 `SendEmailFunction` precedent. ~15-line Function: `[QueueTrigger("%OutboxQueues:GenerateInvoiceQueueName%")] string outboxEventId` → load outbox row by id → deserialize `InvoiceGenerateOutboxPayload` → `await mediator.Send(new IssueInvoice.Command(payload.OrderId), ct)`. **Rejected:** dedicated `IInvoiceGenerationService` (overhead; `IssueInvoice.Command` already exists, idempotent, wired).

### B. Failure handling

4. **SendGrid 30 MB attachment cap → `Error.Permanent(InvoicePdfAttachmentTooLarge)` + outbox stall.** Indicates a rendering bug or data-integrity issue (typical invoice is < 100 KB). Outbox retry policy stalls after 6 attempts; row sits in DB until ops investigates. Customer still sees the order in their account. **Rejected:** graceful degradation without attachment (silently violates "customer always gets PDF" contract); transient retry (cap is fixed, retrying never resolves).

5. **Idempotency inherited from `IssueInvoice.Handler`.** T-0068b AC-5 already pins the handler as idempotent (step 3: `GetByOrderIdAsync` → return existing row verbatim if found). GenerateInvoiceFunction is a thin wrapper, so same Command → same Response. No new Function-level `ProcessedAt` tracking. **Rejected:** Function-level tracking table (redundant; extra DB write per invocation).

### C. Attachment shape + delivery

6. **PDF attachment filename = language-aware.** Use `payload.LanguageCode` (already pre-resolved in `InvoiceGenerateOutboxPayload` per T-0068b Q4): `faktura-{Order.OrderNumber}.pdf` for `cs-CZ`; `invoice-{Order.OrderNumber}.pdf` for `en-US` (and any future locale). Reuses an existing-but-unused payload field; near-zero cost; future-proofs for international expansion. **Rejected:** Czech-only filename (loses the locked-decision rationale from T-0068b's payload design).

7. **Attachment layer = `IEmailSendService` orchestrates; `SendGridEmailProvider` only wires the bytes.** Per ADR adapter discipline: `IEmailSendService.SendOrderPaidCustomerEmailAsync` fetches Invoice + downloads blob + constructs `EmailMessage` with new `Attachment` field. `SendGridEmailProvider.SendAsync` only calls `sgMessage.AddAttachment(...)` if `EmailMessage.Attachment` is non-null. Provider stays a thin SDK wrapper (any future `IEmailProvider` impl could attach). **Rejected:** provider downloads blob itself (tight coupling, violates adapter discipline).

8. **`EmailMessage.Attachment` shape = optional single `Attachment?`** (NOT `IReadOnlyList<Attachment>`). New sealed record `Attachment(string Filename, byte[] Bytes, string MimeType)` at `Core.Domain/Email/Attachment.cs`. T-0069 ships single-PDF attachment only; YAGNI on multi-attachment. **Rejected:** list-of-attachments (every current sender would pass empty-or-single list; speculative).

**PM-absorbed decision (not user-facing):**

- **No new audit-log row** for PDF attachment. System-of-record audit already exists: SendGrid X-Message-Id receipt + blob upload timestamp + Order → Invoice → Email DB relationships (queryable via standard joins). New audit rows would be overhead without operational value. If a future analytics ticket needs the chain visualization, add the row then.

## Scope

### Domain

- **`Core.Domain/Email/Attachment.cs`** — new sealed record `Attachment(string Filename, byte[] Bytes, string MimeType)`. Sanity-check invariants in factory if any (e.g., `Bytes.Length > 0`).
- **`Core.Domain/Email/EmailMessage.cs`** — extend with optional `Attachment? Attachment` field. Default null for all existing senders (auth flows + maker email).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — add 3 new codes:
  - `InvoiceNotYetRendered = "invoice.notYetRendered"` — Transient (email retries).
  - `InvoicePdfAttachmentDownloadFailed = "invoice.pdfAttachmentDownloadFailed"` — Permanent (blob lookup failed).
  - `InvoicePdfAttachmentTooLarge = "invoice.pdfAttachmentTooLarge"` — Permanent (exceeds 30 MB; ops investigation).
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — no new constant (T-0068b already added `InvoiceGenerate`). But add a sibling classifier `IsInvoiceGenerate(string eventType)` to mirror `IsEmailSend` — keeps the dispatcher routing branch explicit.

### AppServices

- **`Core.AppServices/Features/Email/IEmailSendService.cs` + `EmailSendService.cs`** — extend `SendOrderPaidCustomerEmailAsync` (existing T-0067 method):
  - After payload deserialization (existing), call `await invoiceRepository.GetByOrderIdAsync(payload.OrderId, ct)`.
  - If `invoice is null` → return `BusinessResult.Failure<EmailSentReceipt>(Error.Transient(BusinessErrorMessage.InvoiceNotYetRendered))`. The outbox retry policy re-delivers.
  - If `invoice.PdfBlobPath is null` → same (defence: invoice row exists but PDF not yet attached).
  - Else: call `await blobStorage.DownloadAsync(BlobContainer.Invoices, invoice.PdfBlobPath, ct)` to get `byte[]`. Catch exceptions → `Error.Permanent(InvoicePdfAttachmentDownloadFailed)`.
  - Compute filename = `payload.LanguageCode == "en-US" ? $"invoice-{order.OrderNumber}.pdf" : $"faktura-{order.OrderNumber}.pdf"`. (Note: order is currently not loaded — extend the lookup to also fetch the Order's OrderNumber. Cheap single-row read.)
  - Build `Attachment(filename, bytes, "application/pdf")`, construct `EmailMessage` with `Attachment = attachment`, pass to `IEmailProvider.SendAsync`.
- The Validator + DI wiring on `EmailSendService` already exists from T-0067 — only the SendOrderPaidCustomerEmailAsync body changes + the DI gains `IInvoiceRepository` and the `IBlobStorageClient` (latter may already be there for future use; verify).

### Outbox dispatcher

- **`Core.AppServices/Features/Outbox/OutboxDispatcher.cs`** — extend `DispatchDueAsync`:
  - Phase 1 (classify): split events by `OutboxEventTypes.IsEmailSend(event_type)` and `IsInvoiceGenerate(event_type)`. Unrecognized types log `Critical` (existing behaviour).
  - Phase 2 (park-then-commit): unchanged.
  - Phase 3 (publish): for email events call `PublishSendEmailAsync` (existing); for invoice.generate call new `PublishGenerateInvoiceAsync(outboxEventId, ct)`.
- **`Core.Domain/Outbox/IOutboxQueuePublisher.cs`** — add `Task PublishGenerateInvoiceAsync(string outboxEventId, CancellationToken ct)`.
- **`Infra.Functions/Outbox/StorageQueueOutboxPublisher.cs`** (or wherever the impl lives) — implement using the new `OutboxQueues.GenerateInvoiceQueueName` config. Bare outbox id as queue message body per T-0029 pattern (payload stays in Postgres).

### Functions

- **`Makables.Functions/Outbox/GenerateInvoiceFunction.cs`** — new queue-triggered Function:
  ```csharp
  public sealed class GenerateInvoiceFunction(
      IOutboxRepository outbox,
      ISender mediator,
      ILogger<GenerateInvoiceFunction> logger)
  {
      [Function(nameof(GenerateInvoiceFunction))]
      public async Task RunAsync(
          [QueueTrigger("%OutboxQueues:GenerateInvoiceQueueName%")] string outboxEventId,
          CancellationToken cancellationToken)
      {
          var evt = await outbox.GetByIdAsync(outboxEventId, cancellationToken)
              ?? throw new InvalidOperationException($"OutboxEvent {outboxEventId} not found.");
          var payload = JsonSerializer.Deserialize<InvoiceGenerateOutboxPayload>(evt.PayloadJson)
              ?? throw new InvalidOperationException($"Malformed InvoiceGenerateOutboxPayload for {outboxEventId}.");
          var result = await mediator.Send(new IssueInvoice.Command(payload.OrderId), cancellationToken);
          if (!result.IsSuccess)
          {
              logger.LogError("GenerateInvoiceFunction: IssueInvoice failed for outbox {OutboxId}: {Code}",
                  outboxEventId, result.Error!.Code);
              // Re-throw so the queue retry policy fires.
              throw new InvalidOperationException($"IssueInvoice failed: {result.Error.Code}");
          }
          logger.LogInformation("GenerateInvoiceFunction: outbox {OutboxId} → invoice {InvoiceNumber}",
              outboxEventId, result.Value!.InvoiceNumber);
      }
  }
  ```
- **`Makables.Functions/Program.cs`** — register the new Function (auto-discovered by `Microsoft.Azure.Functions.Worker` reflection; no DI change needed beyond `IOutboxRepository` + `ISender` already present).

### Configuration

- **`Core.AppServices/Common/OutboxQueuesOptions.cs`** — add `GenerateInvoiceQueueName` string property (default `"generate-invoice"`).
- **`OutboxQueuesOptionsValidator`** — extend to validate the new queue name (non-empty + matches Azure queue name regex `^[a-z0-9-]{3,63}$`).
- **`local.settings.json` + Azure deployment Bicep** — out of scope for the code PR; the new queue is auto-created by the Azure Storage Queue binding on first publish.

### SendGrid client

- **`Infra.Clients/SendGrid/SendGridEmailProvider.cs`** — extend `SendAsync`: if `message.Attachment is not null`, call `sgMessage.AddAttachment(message.Attachment.Filename, Convert.ToBase64String(message.Attachment.Bytes), message.Attachment.MimeType)`. Wrap in try/catch for size-related errors → translate to `Error.Permanent(InvoicePdfAttachmentTooLarge)` if SendGrid surfaces a 413 / "payload too large" / similar (check exact error from SendGrid SDK).

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — add 3 keys: `invoice.notYetRendered`, `invoice.pdfAttachmentDownloadFailed`, `invoice.pdfAttachmentTooLarge`. Czech translations (admin/log-facing — customer never sees these; they're for the admin audit-log UI in a downstream ticket).

### Tests

- **`Makables.Tests/Functions/Outbox/GenerateInvoiceFunctionTests.cs`** — ~6 tests with NSubstitute mocks: happy path (dispatches IssueInvoice + logs success); outbox not found (throws); malformed payload (throws); IssueInvoice returns Transient (re-throws for retry); IssueInvoice returns Permanent (re-throws for queue dead-letter); CancellationToken propagation.
- **`Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs`** — extend existing test class with ~6 new tests:
  - Order email + invoice not yet rendered → InvoiceNotYetRendered Transient.
  - Order email + invoice rendered + blob download succeeds → email sent with Attachment populated.
  - Order email + blob download throws → InvoicePdfAttachmentDownloadFailed Permanent.
  - Attachment filename = `faktura-{OrderNumber}.pdf` when LanguageCode is `cs-CZ`.
  - Attachment filename = `invoice-{OrderNumber}.pdf` when LanguageCode is `en-US`.
  - SendGrid surfaces size-too-large → InvoicePdfAttachmentTooLarge Permanent.
- **`Makables.Tests/AppServices/Features/Outbox/OutboxDispatcherTests.cs`** — extend with 3 new tests: invoice.generate event routed to PublishGenerateInvoiceAsync (not PublishSendEmailAsync); mixed batch (email + invoice events) → both publishers called; unrecognized event type still logs Critical.
- **`Makables.Tests/Infra/Clients/SendGrid/SendGridEmailProviderTests.cs`** — extend with 2 tests: attachment field on EmailMessage wired into sgMessage; null Attachment → no AddAttachment call.
- **`Makables.IntegrationTests/Outbox/InvoiceGenerateRoutingTests.cs`** — 1 test: full enqueue → dispatcher → faked queue publisher → assert generate-invoice queue was the target.

### Docs

- **`docs/architecture/roles/invoice.md`** — extend Implementation pointer + Lifecycle table to mention T-0069's queue-triggered flow + the email-attachment seam.
- **`docs/architecture/roles/outbox.md`** (if exists, else `outbox.md` is implied via ADR 0020) — update event-type table to include the new `invoice.generate` queue mapping.

### NSwag regen

No public contract changes. No new controllers. NSwag regen NOT required.

## Alternatives Considered

- **Option A — GenerateInvoiceFunction enqueues `order.paid.customerEmail` after rendering.** *Rejected per Q1* — breaks T-0067's atomic-3-events contract; Function emitting side-effect outbox events violates ADR 0020 thin-wrapper pattern.
- **Option B — Pre-load PdfBlobPath into OrderPaidCustomerEmailPayload via patch-existing-outbox-row.** *Rejected per Q1* — introduces cross-row mutation verb on the outbox table; awkward in event-log paradigm.
- **Option C — Two separate emails (one immediately, one with PDF).** *Rejected per Q1* — worse UX, doubles SendGrid cost.
- **Option D — Merged queue (single Function dispatches both email + PDF).** *Rejected per Q2* — loses failure isolation, shared retry budget, one poison stalls both pipelines.
- **Option E — Inline IssueInvoice dispatch from ProcessOutboxFunction (no separate queue).** *Rejected per Q2* — couples render time (~100-500ms) to the polling sweep; loses queue retry semantics.
- **Option F — Dedicated `IInvoiceGenerationService` instead of MediatR.** *Rejected per Q3* — adds a service layer over an already-existing idempotent `IssueInvoice.Command`. Overhead.
- **Option G — Graceful degradation on size-cap failure.** *Rejected per Q4* — silently violates "customer always gets PDF" contract; customer support gets confused complaints.
- **Option H — Function-level ProcessedAt tracking.** *Rejected per Q5* — redundant with IssueInvoice handler's idempotency; extra DB write per invocation.
- **Option I — Czech-only filename.** *Rejected per Q6* — loses T-0068b's LanguageCode-in-payload future-proofing for near-zero implementation cost.
- **Option J — SendGridEmailProvider fetches blob itself.** *Rejected per Q7* — tight coupling between SendGrid adapter and storage; violates ADR adapter discipline.
- **Option K — `IReadOnlyList<Attachment>`.** *Rejected per Q8* — every current sender passes empty-or-single; speculative abstraction.

## Out of scope

- Admin UI for browsing outbox events / re-driving stalled events — downstream admin ticket.
- Customer-facing PDF download endpoint (separate from email attachment) — T-0086 territory.
- Maker-facing invoice download — T-0102 / T-0111 territory.
- Fee invoices (`InvoiceType.Fee` rendering + PayoutBatch FK + email to maker) — T-0101 / T-0102.
- ReverseCharge + StrictFiscalReporting renderer templates — post-MVP (T-0068b returns `InvoicingModeNotImplemented`).
- SendGrid bounce webhook (forwarded email failures) — already deferred from T-0028.
- Renderer streaming surface (LOH allocation per ADR 0025 Performance expectations) — deferred trigger documented; not in this ticket.

## Acceptance criteria

- **AC-1** Given a paid order with the full `MarkOrderPaid` outbox sequence (3 events), when `OutboxDispatcher.DispatchDueAsync` runs, then the `invoice.generate` event is routed to `PublishGenerateInvoiceAsync` (NOT `PublishSendEmailAsync`), AND the email events are routed to `PublishSendEmailAsync`.
- **AC-2** Given an outbox event id is delivered to `GenerateInvoiceFunction` via the queue trigger, when the Function runs, then it loads the outbox row, deserializes the `InvoiceGenerateOutboxPayload`, dispatches `IssueInvoice.Command(payload.OrderId)`, and returns successfully with the invoice number logged.
- **AC-3** Given GenerateInvoiceFunction processes the same outbox event twice (queue redelivery), when the second invocation runs, then `IssueInvoice.Handler` returns the existing invoice's values (per T-0068b AC-5), the Function logs success, and no new invoice number is allocated.
- **AC-4** Given a customer order-paid email event is queued BEFORE the invoice has been rendered, when `EmailSendService.SendOrderPaidCustomerEmailAsync` runs, then it returns `BusinessResult.Failure(Error.Transient(InvoiceNotYetRendered))`, the outbox retry policy re-delivers the event after the first interval (1m), and on the second attempt (after invoice rendered) the email sends with the PDF attached.
- **AC-5** Given a customer order-paid email + a rendered invoice with `PdfBlobPath` populated, when the email handler runs, then `IBlobStorageClient.DownloadAsync(BlobContainer.Invoices, invoice.PdfBlobPath)` returns the PDF bytes, `EmailMessage.Attachment` is populated as `Attachment(filename, bytes, "application/pdf")`, and `SendGridEmailProvider` calls `sgMessage.AddAttachment(...)` with those bytes.
- **AC-6** Given the customer's `LanguageCode == "cs-CZ"`, when the email handler builds the attachment, then `Attachment.Filename == "faktura-{Order.OrderNumber}.pdf"`. Given `LanguageCode == "en-US"`, then `Filename == "invoice-{Order.OrderNumber}.pdf"`.
- **AC-7** Given the PDF bytes exceed 30 MB, when `SendGridEmailProvider.SendAsync` calls SendGrid, then the response surfaces as `BusinessResult.Failure(Error.Permanent(InvoicePdfAttachmentTooLarge))`, the outbox retry policy stalls after 6 attempts, and ops audit log shows the failure.
- **AC-8** Given `IBlobStorageClient.DownloadAsync` throws (blob deleted mid-flight or transient network error), when the email handler catches it, then it returns `BusinessResult.Failure(Error.Permanent(InvoicePdfAttachmentDownloadFailed))`. (Permanent because a missing blob is a data-integrity issue worth ops attention; transient network errors at the blob layer are already retried internally by the Azure Storage SDK.)
- **AC-9** Given the auth-flow emails (magic link, email confirmation, password reset), when they ship via the existing `SendAuthEmailAsync` path, then `EmailMessage.Attachment == null` and no attachment-related code runs. (Regression pin: T-0028 + T-0067 behaviour preserved byte-for-byte.)
- **AC-10** Given the `OrderPlacedMakerEmail` event (maker notification), when it ships, then `EmailMessage.Attachment == null`. (Maker email does NOT carry the invoice PDF; only the customer does.)
- **AC-11** Build clean. Unit tests: baseline (1132 after T-0068b folds) + ~17 new. Integration tests: baseline (147) + 1 new. Consistency script exit 0.
- **AC-12** `node scripts/check-consistency.mjs` exit 0 (no new T1–T7 violations vs the 101-tracked baseline from T-0068b).
- **AC-13** Role doc updated (Implementation pointer + Lifecycle).

## Technical notes

### Why retry-based eventual consistency works

The outbox retry policy starts at 1 minute. A typical QuestPDF render of a single-line invoice is < 500ms; blob upload is < 1 second; `IssueInvoice.Handler` end-to-end is well under 3 seconds. So if the email queue beats the invoice queue (Azure queue ordering is FIFO per partition but cross-queue ordering is not guaranteed), the first email retry at +1 minute almost always succeeds. The retry budget burns ~1 attempt per delayed delivery — well within the 6-attempt cap.

### Why the email handler fetches both Invoice AND Order

`IEmailSendService.SendOrderPaidCustomerEmailAsync` currently has the customer Order via the payload (T-0067 pre-bakes `OrderId`, `OrderNumber`, etc.). It does NOT currently load the Invoice. T-0069 extends it to load the Invoice via `IInvoiceRepository.GetByOrderIdAsync` (single indexed query). The Order's `OrderNumber` is already in the payload — no second Order fetch needed.

### Why filename language matters more than PDF body language

The PDF body is single-language (Czech) at MVP per T-0068b's renderer design. But the filename appears in the customer's email client preview pane BEFORE they open the attachment. A Czech customer sees "faktura-M-CZ-...pdf" — recognisable. An English customer (post-MVP, hypothetical) would see "invoice-..." — also recognisable. Tiny cost, big future-proofing.

### Why InvoicePdfAttachmentDownloadFailed is Permanent (not Transient)

The Azure Storage SDK already handles transient network blips with internal retry. A failure surfaced to user code means: blob missing (data-integrity bug) OR auth misconfigured (deploy bug) OR Azure outage beyond SDK retry budget (rare). All three are ops-investigation territory; retrying via outbox at 1m wastes budget and noisy-logs. Permanent + outbox stall + Critical log gets the right person paged.

### Why GenerateInvoiceFunction throws instead of returning BusinessResult

Azure Functions queue triggers signal retry via thrown exceptions. The Function MUST throw on any failure path (idempotency-respecting per T-0068b means the throw is safe). Catching `IssueInvoice` failures and returning normally would silently swallow them.

### Manual deployment steps

1. **`generate-invoice-queue-config`** — at first deploy, ensure `OutboxQueues:GenerateInvoiceQueueName` is set in Azure App Configuration (or `local.settings.json` for dev). Default `"generate-invoice"` works; override only if the Azure Storage namespace already has a conflicting queue. The Azure Storage Queue binding auto-creates the queue on first publish — no manual `az storage queue create` needed. **Owner:** PM (default works for current environments). **Blocker:** no — works out of the box.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Email/Attachment.cs`
- `backend/src/Makables.Functions/Outbox/GenerateInvoiceFunction.cs`
- `backend/src/Makables.Tests/Functions/Outbox/GenerateInvoiceFunctionTests.cs`
- `backend/src/Makables.IntegrationTests/Outbox/InvoiceGenerateRoutingTests.cs`

### Modified (domain)
- `backend/src/Makables.Core.Domain/Email/EmailMessage.cs` — add `Attachment? Attachment` field.
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — 3 new codes.
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` — add `IsInvoiceGenerate(string)` classifier method.

### Modified (appservices)
- `backend/src/Makables.Core.AppServices/Features/Email/IEmailSendService.cs` + impl — extend `SendOrderPaidCustomerEmailAsync` with Invoice lookup + blob download + Attachment construction.
- `backend/src/Makables.Core.AppServices/Features/Outbox/OutboxDispatcher.cs` — branch routing per event type.
- `backend/src/Makables.Core.AppServices/Common/OutboxQueuesOptions.cs` + validator — add `GenerateInvoiceQueueName`.

### Modified (infra)
- `backend/src/Makables.Core.Domain/Outbox/IOutboxQueuePublisher.cs` — add `PublishGenerateInvoiceAsync` method.
- `backend/src/Makables.Infra.Functions/Outbox/StorageQueueOutboxPublisher.cs` (or wherever the impl lives) — implement the new method.
- `backend/src/Makables.Infra.Clients/SendGrid/SendGridEmailProvider.cs` — `AddAttachment` wiring + size-error translation.

### Modified (tests + docs + i18n)
- `backend/src/Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs` — +6 tests.
- `backend/src/Makables.Tests/AppServices/Features/Outbox/OutboxDispatcherTests.cs` — +3 tests.
- `backend/src/Makables.Tests/Infra/Clients/SendGrid/SendGridEmailProviderTests.cs` — +2 tests.
- `frontend/src/lib/i18n/cs-CZ.ts` — 3 new keys.
- `docs/architecture/roles/invoice.md` — update Lifecycle.
- `docs/tickets/INDEX.md` — status update to in_progress → in_review → done.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0069.md`.

## Status log

- 2026-06-06 `draft` by PM. Created from INDEX line; awaiting T-0068b merge.
- 2026-06-07 `draft → ready` by PM. T-0068b merged at commit 648b8de; T-0029 already done. User answered 8 blocking decisions via AskUserQuestion per `/feature` workflow step 3 (lookup-at-send-time + retry; separate generate-invoice queue; MediatR dispatch; Permanent + stall on 30 MB cap; idempotency inherited; language-aware filename; EmailSendService orchestrates attachment; single Attachment? field). Decisions captured in `## Locked design decisions`. One non-user-facing PM decision absorbed (no new audit-log row). One `manual_steps` flagged (generate-invoice-queue-config) — NOT a PR-open blocker (Azure Storage Queue binding auto-creates). **Ready for dotnet-backend.**
