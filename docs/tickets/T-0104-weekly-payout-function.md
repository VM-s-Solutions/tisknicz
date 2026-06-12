---
id: T-0104
title: RunWeeklyPayoutBatch Function (timer Monday 02:00 UTC + HTTP escape hatch)
status: ready
size: S
owner: dotnet-backend
created: 2026-06-12
updated: 2026-06-12
depends_on: [T-0102]
blocks: []
user_stories: [US-admin-0007]
adrs: [0009, 0020]
phase: 5
manual_steps: []
security_touching: false
layers: [infra-functions]
---

# T-0104 — RunWeeklyPayoutBatch Function (timer Monday 02:00 UTC + HTTP escape hatch)

## Context

T-0104 is the **scheduler ticket of the payout bundle** (T-0101 PayoutBatch entity → T-0102 CreatePayoutBatch writer → T-0102a `MakablesMeters.Payouts` instrumentation → T-0103 MarkPayoutBatchCompleted + `payout-sent` emails → T-0104 this Function). Per ADR 0020 the Function is a **thin trigger wrapper only**: it dispatches `CreatePayoutBatch.Command` via `ISender`, interprets the `BusinessResult`, and logs. Every business decision — claim predicate, exclusions, fee invoices, batch numbering, CSV generation — lives in the T-0102 writer.

The shape is a hybrid of two locked precedents. From **T-0029 `ProcessOutboxFunction`**: dual trigger — timer (`%RunWeeklyPayoutBatch:Schedule%`, `UseMonitor = true`) plus an HTTP escape hatch (`POST`, `AuthorizationLevel.Function`) so ops can force a batch run without waiting for Monday. From **T-0077 `AutoDeliverOrdersFunction`**: thin MediatR dispatch + structured summary log. Unlike T-0077/T-0083 there is **no per-row enumeration loop** — the Function sends exactly one Command per tick, so the Q-0008 MARS workaround does not apply here.

Idempotency lives entirely in the writer: a re-fire while a `Processing` batch is open returns the existing batch (`AlreadyExisted = true`), never creates a second one. A week with nothing to pay out returns `payoutBatch.empty` — for this Function that is a **normal quiet week**, logged at Information, never Warning. The Function therefore has exactly four response branches and zero state of its own.

## Locked design decisions

Captured per `docs/process/deliberation.md`. Q1–Q5 were user-locked in the 2026-06-12 bundle deliberation; they are **owned by T-0102** (the writer) but define the response contract this Function consumes.

### A. User-locked (2026-06-12 bundle deliberation; owned by T-0102, consumed here)

1. **Q1 — CSV is a generic documented format behind `IPayoutCsvFormatter`** (keyed-service-ready format seam; columns: account, amount as CZK decimal display, VS = batch/order number, message). Bank-native exporters are follow-up tickets. *T-0104 impact:* none — CSV generation is inside the writer; the Function never touches the blob.
2. **Q2 — Fee invoices per maker per batch at CreatePayoutBatch** (one `InvoiceType.Fee` invoice; DUZP = batch creation date; shared FV-CZ sequence per T-0068a lock 4). *T-0104 impact:* a successful dispatch implies invoices were issued atomically; no compensating logic in the Function.
3. **Q3 — Partially-refunded Delivered orders (`RefundedAmountMinor > 0`) are EXCLUDED from auto-claim** — they stay unclaimed, surface in the batch response + audit, and ride the next batch after admin resolution. *T-0104 impact:* the Function logs the excluded-partially-refunded count from the response.
4. **Q4 — Batch is IMMUTABLE once created (`Processing`).** No order removal (fee invoices are already issued and legally immutable); whole-batch cancel is a deferred follow-up. *T-0104 impact:* no retry/repair branch in the Function — a created batch is final.
5. **Q5 — Orders of makers with NULL `BankAccount` are EXCLUDED from claim**; the excluded-maker count surfaces in the response + audit. *T-0104 impact:* the Function logs the excluded-maker count from the response.

### B. Precedent/ADR-locked (no relitigation)

- **ADR 0020 (Functions discipline).** Thin wrapper; no business logic in `Makables.Functions/*.cs`; schedule in app configuration. Dual trigger per the ProcessOutboxFunction precedent, including `UseMonitor = true` on the timer (schedule persists across host restarts; one tick per schedule across scale-out) and `AuthorizationLevel.Function` on the HTTP trigger.
- **ADR 0009 + T-0062/T-0068a (numbering + TZ-aware local date).** Batch number `VYP-CZ-YYYY-Www` (ISO week) via the existing `IPayoutBatchNumberGenerator`; the **writer** derives the local date TZ-aware (Europe/Prague) per the T-0062/T-0068a precedent. The Function passes nothing — `CreatePayoutBatch.Command` is parameterless; the handler uses `IClock`.
- **ADR 0014 (UoW pipeline).** Claim + batch insert + fee invoices commit atomically in ONE UoW (pipeline commits — no `SaveChangesAsync()` anywhere). The Function sees only the final `BusinessResult`.
- **No MARS concern.** Single Command dispatch, no enumeration loop — Q-0008 does not apply (contrast T-0077/T-0083).

### C. PM-absorbed (no user input needed)

- **Schedule:** key `RunWeeklyPayoutBatch:Schedule`, default `0 0 2 * * 1` (Monday 02:00 UTC). Independently tunable from all existing schedules. On Mondays it coincides with `CancelExpiredPendingPaymentOrders` (daily 02:00 UTC) — acceptable: both are thin, touch disjoint aggregates, and either can be re-timed via its key without a code change.
- **Response interpretation (the Function's whole job):**
  - **Created** → `LogInformation` with `BatchNumber`, `OrderCount`, `MakerCount`, `TotalAmountMinor`/`Currency`, excluded partially-refunded order count (Q3), excluded NULL-bank-account maker count (Q5).
  - **`AlreadyExisted = true`** → `LogInformation` "open Processing batch returned" with its `BatchNumber` (idempotent Monday re-fire / manual + timer double-fire safe).
  - **Failure `payoutBatch.empty`** → `LogInformation` "no payable orders this week" (normal quiet week — NOT a Warning; the writer records the attempt in the audit log, writes NO batch row).
  - **Any other failure** → `LogError` with the `BusinessErrorMessage` code.
- **HTTP escape hatch:** `POST /api/payouts/run-batch`, `AuthorizationLevel.Function`. Returns `200 OkObjectResult(response)` on success/AlreadyExisted; `422 UnprocessableEntity` + error envelope on `payoutBatch.empty`; `500` + error code otherwise. Function-key-only ops surface — NOT a public contract, **no NSwag regen**.
- **Function shape:** `Makables.Functions/Payouts/RunWeeklyPayoutBatchFunction.cs`, primary-constructor DI (`ISender mediator, ILogger<RunWeeklyPayoutBatchFunction> logger`), constants `TimerFunctionName = "RunWeeklyPayoutBatchTimer"` / `HttpFunctionName = "RunWeeklyPayoutBatchHttp"`, both triggers delegating to one private dispatch-and-interpret method. `IsPastDue` → Information note, run anyway (missed Monday tick still pays makers that morning).
- **No instrumentation here.** `MakablesMeters.Payouts` counters land in T-0102a per ADR 0023. `payout-sent` settlement emails stay in T-0103 (bundle PR #2). Fee-invoice **maker** email (T-0069 attachment pattern) is enqueued by the T-0102 writer at batch creation.
- **Bundle note (owned by T-0102's leading commit, NOT this ticket):** Q-0017 data-fix migration UPDATEing all 16 single-brace email-template subject rows (SeedOrderEmailTemplates ×4, ShippingPipelineBundle ×4, DeliveryCloseBundle ×2, OrderCleanupBundle ×6 — grep `subject` in those migrations for single-brace `{order_number}`-style values, fix to double-brace) opens the bundle PR.

## Scope

- **NEW `backend/src/Makables.Functions/Payouts/RunWeeklyPayoutBatchFunction.cs`** (~60–70 lines incl. XML doc):
  - `[Function(TimerFunctionName)] RunTimer([TimerTrigger("%RunWeeklyPayoutBatch:Schedule%", UseMonitor = true)] TimerInfo timer, CancellationToken ct)` — past-due note, then dispatch-and-interpret. No throw on business failure (an Error log is the alert surface; a Function-level throw adds nothing per the ProcessOutbox comment precedent).
  - `[Function(HttpFunctionName)] RunHttp([HttpTrigger(AuthorizationLevel.Function, "post", Route = "payouts/run-batch")] HttpRequestData _, CancellationToken ct)` — same dispatch, shapes the HTTP response per §C.
  - Private core: `await mediator.Send(new CreatePayoutBatch.Command(), ct)` once; four-branch interpretation per §C.
- **MODIFIED `backend/src/Makables.Functions/local.settings.json`** — add `"RunWeeklyPayoutBatch:Schedule": "0 0 2 * * 1"`.
- **MODIFIED `docs/deployment/env-vars.md`** — add the schedule row: `Monday 02:00 UTC — weekly maker payout batch (CreatePayoutBatch). T-0104.`
- **Tests (NEW `RunWeeklyPayoutBatchFunctionTests`, ~4):** mocked `ISender` —
  1. Created response → `Send` called exactly once; Information log carries batch number + totals + both exclusion counts; no Warning/Error.
  2. `AlreadyExisted = true` → Information log; no second `Send`; no Warning/Error.
  3. `payoutBatch.empty` failure → Information log (quiet week); no Warning/Error.
  4. Other failure code → Error log with the code; no throw from the timer path.

No new entities, features, repositories, migrations, error codes, i18n keys, or HTTP endpoints on the Web.* hosts. No NSwag regen.

## Alternatives Considered

- **Option A — Function performs the claim/CSV/invoice work itself.** *Rejected per ADR 0020* — skips Validator + UoW pipeline + audit; the writer (T-0102) owns all business logic. Thin dispatch is the locked T-0077/T-0083 pattern.
- **Option B — Timer-only, no HTTP escape hatch.** *Rejected per ProcessOutbox precedent* — a missed Monday tick (host down, bad deploy) would delay maker payouts a full week. Function-key `POST` lets ops force the run; writer idempotency makes the double-fire harmless.
- **Option C — Admin-JWT endpoint on `Web.Admin` instead of the Function HTTP trigger.** *Rejected here* — the admin-facing "run batch now" button is the T-0102/T-0118 surface dispatching the same Command under ADR 0014 audit. The Function-level hatch is an ops tool living next to the timer it substitutes for, per the ProcessOutbox precedent. Both coexist; neither duplicates business logic.
- **Option D — Log `payoutBatch.empty` as Warning.** *Rejected per §C* — an empty week is expected at MVP volumes; Warning-on-normal trains alert fatigue. The writer's audit record is the trace; Information suffices.
- **Option E — Daily schedule with "is it Monday?" guard in the Function.** *Rejected* — calendar logic in the Function violates ADR 0020's thin-wrapper rule; NCRONTAB already expresses weekly (`0 0 2 * * 1`).

## Out of scope

- **Everything inside the writer** — claim predicate, Q1 CSV formatter + blob write (`payouts/{cc}/{batchNumber}.csv`), Q2 fee invoices + maker email, Q3/Q5 exclusions, Q4 immutability, batch numbering, audit record, `payoutBatch.empty` error code definition: **T-0102**.
- **`MakablesMeters.Payouts` instrumentation** — T-0102a per ADR 0023.
- **`MarkPayoutBatchCompleted` + `payout-sent` settlement emails** — T-0103 (bundle PR #2).
- **Bank-native CSV exporters** — follow-up tickets once the operator names the bank (Q1).
- **Whole-batch cancel** — deferred follow-up (Q4).
- **Q-0017 data-fix migration** — leading commit of the bundle PR under T-0102.
- **Frontend / NSwag** — no public contract change.

## Acceptance criteria

- **AC-1** Given the Functions host loads app settings, when the timer trigger resolves, then the schedule comes from `%RunWeeklyPayoutBatch:Schedule%` (default `0 0 2 * * 1` = Monday 02:00 UTC) with `UseMonitor = true`, and each tick dispatches `CreatePayoutBatch.Command` via `ISender` **exactly once**. The Function contains no claim/CSV/invoice logic.
- **AC-2** Given the writer returns a created batch, when the tick completes, then an Information log carries `BatchNumber` (`VYP-CZ-YYYY-Www`), `OrderCount`, `MakerCount`, `TotalAmountMinor` + `Currency`, the excluded partially-refunded order count (Q3), and the excluded NULL-bank-account maker count (Q5). No Warning or Error logs.
- **AC-3** Given an open `Processing` batch already exists (Monday re-fire or manual+timer double-fire), when the tick runs, then the response has `AlreadyExisted = true`, an Information log names the existing batch, and **no second batch row exists** (writer-guaranteed; the Function adds no defence of its own).
- **AC-4** Given no payable orders this week, when the writer returns failure `payoutBatch.empty`, then the Function logs **Information** ("no payable orders"), not Warning or Error, and the timer path does not throw.
- **AC-5** Given the writer returns any other failure code, when the tick completes, then the Function logs **Error** with the `BusinessErrorMessage` code; the timer path still does not throw (the Error log is the alert surface).
- **AC-6** Given `POST /api/payouts/run-batch` with a valid function key, when invoked, then the same Command is dispatched and the response is `200` + response body (created/AlreadyExisted), `422` + error envelope (`payoutBatch.empty`), or `500` + error code (other). Anonymous requests are rejected by the platform (`AuthorizationLevel.Function`).
- **AC-7** Build clean; ~4 new Function tests green; `local.settings.json` + `docs/deployment/env-vars.md` carry the schedule key; `node scripts/check-consistency.mjs` exit 0; no NSwag diff.

## Risk / mitigation

- **Risk: timer + manual HTTP double-fire creates two batches.** *Mitigation:* writer idempotency (open `Processing` batch returned, `AlreadyExisted = true`) — AC-3. The Function needs no lock of its own.
- **Risk: missed Monday tick delays payouts a week.** *Mitigation:* `UseMonitor = true` past-due catch-up + the HTTP escape hatch; unclaimed Delivered orders simply ride the next run (the predicate is the claim — stateless re-fetch per T-0029/T-0077 precedent).
- **Risk: Monday 02:00 UTC coincides with the daily CancelExpiredPendingPaymentOrders sweep.** *Mitigation:* disjoint aggregates, both thin; either schedule retunes via its config key without code change.
- **Risk: quiet-week noise pages ops.** *Mitigation:* `payoutBatch.empty` is Information by contract (AC-4); only unexpected codes reach Error.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0104.md` — the writer's heavy test inventory lives in T-0102.

## Files touched (expected)

### New
- `backend/src/Makables.Functions/Payouts/RunWeeklyPayoutBatchFunction.cs`
- `backend/src/Makables.Tests/Functions/Payouts/RunWeeklyPayoutBatchFunctionTests.cs`

### Modified
- `backend/src/Makables.Functions/local.settings.json` — `RunWeeklyPayoutBatch:Schedule` key.
- `docs/deployment/env-vars.md` — schedule row.

## Commits hint

1. `test(T-0104): pin RunWeeklyPayoutBatch response interpretation (red)` — 4 Function tests against mocked `ISender`.
2. `feat(T-0104): RunWeeklyPayoutBatch Function (timer Monday 02:00 UTC + HTTP escape hatch) + schedule config` — Function + local.settings key + env-vars row.

## Definition of Ready

- [ ] T-0102 merged (or earlier in the same bundle branch) with: parameterless `CreatePayoutBatch.Command`, response carrying `BatchNumber`, `AlreadyExisted`, `OrderCount`, `MakerCount`, `TotalAmountMinor`/`Currency`, and both exclusion counts (exact field names verified against the writer at implementation time), and `BusinessErrorMessage` code `payoutBatch.empty`.
- [ ] `IPayoutBatchNumberGenerator` (T-0101) on the branch — consumed by the writer, referenced only in logs here.
- [ ] Schedule key name agreed (`RunWeeklyPayoutBatch:Schedule`) — no collision in `local.settings.json`.

## Status log

- 2026-06-12 `draft` by PM. Created as the scheduler ticket of the payout bundle (T-0101/T-0102/T-0102a/T-0103/T-0104). Precedents: T-0029 ProcessOutboxFunction (dual trigger, `UseMonitor`, `AuthorizationLevel.Function`, no-throw-on-business-failure), T-0077 AutoDeliverOrdersFunction (thin dispatch + structured summary log). Slice scope: one Function file + schedule config + env-vars row + ~4 mocked-ISender tests. Zero business logic, zero new endpoints on Web.* hosts, zero NSwag.
- 2026-06-12 `draft → ready` by BA. Q1–Q5 user-locked in the 2026-06-12 bundle deliberation recorded in §A with per-ticket ownership (writer owns all five; T-0104 consumes the response contract). PM-absorbed defaults captured in §C: four-branch response interpretation (created/AlreadyExisted/empty/other), `payoutBatch.empty` = Information not Warning, schedule `0 0 2 * * 1` under `RunWeeklyPayoutBatch:Schedule`, HTTP escape-hatch status mapping, no MARS concern (single dispatch), instrumentation deferred to T-0102a, Q-0017 data-fix owned by T-0102's leading commit. **Ready for dotnet-backend** once the DoR boxes tick on the bundle branch.
