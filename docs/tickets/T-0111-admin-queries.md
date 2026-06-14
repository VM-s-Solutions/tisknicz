---
id: T-0111
title: Admin unscoped paged read queries — orders, invoices, audit log
status: ready
size: M
owner: dotnet-backend
created: 2026-06-14
updated: 2026-06-14
depends_on: [T-0080, T-0105]
blocks: [T-0118]
user_stories: [US-admin-0009, US-admin-0012, US-admin-0015]
adrs: [0013, 0014, 0023]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-admin]
---

# T-0111 — Admin unscoped paged read queries — orders, invoices, audit log

## Context

T-0111 is the **first ticket in the admin-tooling bundle** (T-0111 read queries → T-0109 outbox retry/ack → T-0108 country-config provider change → T-0110 GDPR hard-delete), shipped under one PR in risk-ascending order. It is intentionally the read-only entry: three paged, filtered, `Unscoped()` admin list queries that double as the **verification harness** for the three mutations that follow. After T-0111 lands, the integration tests for T-0109/T-0108/T-0110 can assert their side effects by reading them back through these admin list endpoints (e.g. T-0110's anonymised order snapshots surface through `GET /api/v1/admin-orders`; T-0109's acknowledged outbox rows drop out of the stalled set; the audit row every mutation writes surfaces through `GET /api/v1/audit-log`).

This ticket directly satisfies three admin stories: **US-admin-0009 — View all orders + filter** AC-1 (paginated, all orders across all makers + customers, sorted `CreatedAt DESC`, filters: state, country, maker, customer email), **US-admin-0012 — View invoices list** AC-1 (paginated, all invoices, filters: type, country, recipient, date range), and **US-admin-0015 — View admin audit log** AC-1 (paginated, sorted `created_at DESC`, filters: admin user, action code, target entity, date range). The detail pages (US-admin-0009 AC-2 audit-trail tab; US-admin-0012 AC-2 PDF download; US-admin-0015 AC-2 before/after diff) are **out of scope** — invoice download already exists (T-0102b admin CSV / T-0088 invoice PDF precedent), and the diff/detail rendering is a frontend concern (T-0118).

The locked precedents are all on master or in the order-queries bundle: `PagedData<T>` (T-0043), page-based pagination + clamp `[1, 50]` (T-0080), one-file feature shape (every Phase 4/5 ticket), AsNoTracking projection-only EF reads with `IgnoreAutoIncludes()` (T-0080 `OrderQueries`), the read-side `IOrderQueries` split from the write-scoped `IOrderRepository` per ADR 0023, globally-unique Response naming (PR #38 NSwag fix), and the admin-host controller convention (T-0105 `OrdersController`: `[Authorize]` under the admin audience, one-liner Mediator dispatch). T-0111's job is to apply them to the **admin read surface**, where the scoping inverts: instead of an owner predicate baked into the query (the T-0080 IDOR shield), these queries call `IOrderRepository.Unscoped()` / `IInvoiceRepository.Unscoped()` — admin sees **everything**, ignoring per-owner filters by design (ADR 0013 §"the Unscoped escape hatch is admin-host only"). The audit-log query reads `AdminAuditLogEntry` directly (it has no owner scope — it IS the cross-admin activity record).

The admin order DTO is **privileged**: unlike the maker-facing T-0081 list (which omits customer email per GDPR data minimisation), the admin row carries `CustomerEmail` + `MakerName` because admin is the privileged actor — there is no redaction at the admin surface (US-admin-0009 AC-1 names "customer email" as a filter, which presupposes admin can see it). This is a deliberate, story-locked divergence from the maker surface and is the reason `security_touching: true` — every reviewer pass must confirm these unscoped reads are reachable **only** from `Web.Admin` (the admin-audience JWT enforcement on the host is the boundary; ADR 0013).

This is a read-only ticket: **no new `BusinessErrorMessage` codes**, no migrations, no outbox events, no email, no i18n keys, no commands. The only failure modes are the Validator clamps (`Page < 1`, `PageSize ∉ [1, 50]`, inverted date range), which surface as 400 through the existing FluentValidation envelope. Soft-deleted rows stay hidden by the global `Auditable` query filter by default; `.IgnoreQueryFilters()` is applied **only** where an AC needs soft-deleted/anonymised rows visible (the admin-orders query, so T-0110's anonymised + soft-deleted reconciliation rows surface), and that single call is commented in the EF impl.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the relevant dimension at the 2026-06-14 bundle deliberation (Q-E: filter set = exactly the AC-named dimensions, no more). PM-absorbed decisions follow from the T-0080 order-queries precedent.

### A. User-locked at the 2026-06-14 deliberation (non-negotiable)

1. **Filter set = exactly the AC-named dimensions, no more (Q-E).** Each query exposes precisely the filters its story AC names — no speculative additions:
   - `GetAllOrdersPagedAsync`: **state, country, makerId, customerEmail** (US-admin-0009 AC-1).
   - `GetAllInvoicesPagedAsync`: **type (Customer/Fee), country, recipient, dateRange** (US-admin-0012 AC-1).
   - `GetAdminAuditLogPagedAsync`: **adminUserId, actionCode, targetEntity, dateRange** (US-admin-0015 AC-1).
   **Rejected:** a shared "kitchen-sink" `AdminFilter` record spanning all three surfaces (couples three independently-evolving filter sets; forces every query to ignore fields it doesn't use); adding free-text search across orders/invoices at MVP (no story names it; needs indexed-search infra — same rejection as T-0080 A.2).

2. **`Unscoped()` from `Web.Admin` only.** All three queries read across every tenant — admin is the privileged actor (ADR 0013 §"Unscoped escape hatch is admin-host only"). The admin-audience JWT enforcement on the host is the boundary; a customer/maker JWT cannot replay against the admin host. **Rejected:** owner-scoped reads (defeats the entire purpose — admin needs the cross-tenant view); a per-request "impersonate owner" toggle (out of scope; admin sees all, full stop).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT enforcement + scoped repositories).** The three endpoints run under the `Web.Admin` host audience; an admin JWT (`aud=admin`) is required. The reads use `IOrderRepository.Unscoped()` / `IInvoiceRepository.Unscoped()` — the documented admin-only escape hatch. The Reviewer rejects any `Unscoped()` call reachable from `Web.Customer` / `Web.Maker` / `Web.Public`. The audit-log read has no owner scope by construction.
- **ADR 0014 (UoW pipeline + admin audit).** `ValidationPipelineBehavior` runs on every request; `UnitOfWorkPipelineBehavior` runs on commands only. These are **queries** — read-only Handlers, no `SaveChangesAsync()`, no state mutation, and (unlike the bundle's three mutations) **no** `IAdminAuditableCommand` — reads are not audited (only admin **writes** are audited per ADR 0014; reading the order list is not a recordable action).
- **ADR 0023 (read-side queries split from write-side repositories).** New `IAdminQueries` interface lives at `Core.Domain/Admin/IAdminQueries.cs`. The three write-scoped repositories (`IOrderRepository`, `IInvoiceRepository`) stay write/CRUD-scoped; the admin read projections are AsNoTracking, projection-only, and live behind the new interface. Mirrors the T-0080 `IOrderQueries` / `IOrderRepository` split. The EF impl composes over the existing `Unscoped()` queryables rather than re-declaring `DbSet` access.
- **One-file feature shape.** Each of `GetAllOrders.cs`, `GetAllInvoices.cs`, `GetAdminAuditLog.cs` contains nested `Query`, `Validator`, `Handler`, and a globally-unique `Response`. No separate files per type.
- **`BusinessResult<T>` for expected failures.** Validator failures (Page < 1, PageSize out of range, inverted date range) surface as 400 via the existing validation envelope. No `BusinessErrorMessage` code introduced — cross-tenant reads simply return rows; there is no domain failure mode (an empty result is success with `TotalCount = 0`, not 404).

### C. PM-absorbed (no user input needed)

- **Read-side interface:** new `IAdminQueries` at `Core.Domain/Admin/IAdminQueries.cs` with the three paged methods. `IOrderRepository` / `IInvoiceRepository` stay write-scoped (ADR 0023, mirrors T-0080).
- **EF projection:** `Select` projection to DTO directly in EF (no entity materialisation). `AsNoTracking()` + `IgnoreAutoIncludes()` on every query. Composes over `db.Orders` / `db.Invoices` (admin reads everything; the `Unscoped()` semantic is the absence of an owner predicate). Audit-log composes over the `AdminAuditLogEntry` set (no soft-delete filter — that entity is not `Auditable`).
- **`.IgnoreQueryFilters()` scope:** applied **only** on `GetAllOrdersPagedAsync` (admin needs T-0110's soft-deleted + anonymised reconciliation rows — US-admin-0009 admin view shows everything). A single commented call. `GetAllInvoicesPagedAsync` keeps the global filter (invoices are never soft-deleted post-issuance per role/invoice.md; the AC doesn't need deleted rows). `AdminAuditLogEntry` has no soft-delete filter to ignore.
- **Sort:** all three default `CreatedAt DESC` (the audit entity's column is `CreatedAt`; invoices sort by `CreatedAt` too — US-admin-0012 AC-1 names `IssueDate DESC`, but `Invoice.IssueDate` and `CreatedAt` coincide at issuance; this ticket sorts by `CreatedAt DESC` for cross-query consistency, with a tie-breaker on `Id`). No `*Sort` enum is introduced — the admin lists have one canonical sort at MVP (Q-E: no speculative dimensions). New sort modes append behind a future enum if usage warrants.
- **PagedData<T>:** existing sealed record on master is the envelope. Two round-trips (`CountAsync` + `Skip/Take` projection) per the T-0080 precedent (accurate total count; avoids the `COUNT(*) OVER ()` full-scan trap).
- **PageSize clamp:** Validator enforces `Page >= 1` and `PageSize ∈ [1, 50]` (default 20). Fast-fail with 400 (no silent clamp — T-0080 J precedent).
- **Globally-unique Response naming:** `GetAllOrdersResponse`, `GetAllInvoicesResponse`, `GetAdminAuditLogResponse` (PR #38 NSwag fix).
- **DI registration:** `services.AddScoped<IAdminQueries, AdminQueries>()` in `AddMakablesInfrastructure.cs`.
- **Admin authorization:** `[Authorize]` (admin scheme) on every endpoint — JWT audience enforced per host per ADR 0013. No owner resolution (admin sees all).
- **NSwag regen:** admin host only.
- **Q-0011 (rate-limit on read endpoints):** TOUCHED not closed. These are admin-JWT-gated (2 trusted users) — lower spam risk than the customer surface Q-0011 was raised against. Kept open as a standalone secops follow-up; flagged for secops Gate 3 re-confirmation in the bundle. **No scope expansion in this ticket.**

## Scope

### Domain layer

- **`Core.Domain/Admin/IAdminQueries.cs`** — NEW interface (read-side, ADR 0023):
  ```csharp
  public interface IAdminQueries
  {
      Task<PagedData<AdminOrderListItemDto>> GetAllOrdersPagedAsync(
          AdminOrderFilter filter, int page, int pageSize, CancellationToken ct);

      Task<PagedData<AdminInvoiceListItemDto>> GetAllInvoicesPagedAsync(
          AdminInvoiceFilter filter, int page, int pageSize, CancellationToken ct);

      Task<PagedData<AdminAuditLogItemDto>> GetAdminAuditLogPagedAsync(
          AdminAuditLogFilter filter, int page, int pageSize, CancellationToken ct);
  }
  ```

### AppServices layer

- **`Core.AppServices/Features/Admin/Filters/AdminOrderFilter.cs`** — NEW sealed record:
  ```csharp
  public sealed record AdminOrderFilter(
      OrderState? State,
      string? CountryCode,
      string? MakerId,
      string? CustomerEmail);
  ```
  All fields nullable — any subset. `CustomerEmail` matches case-insensitively against the order's snapshot contact email (the privileged admin view; US-admin-0009 AC-1).
- **`Core.AppServices/Features/Admin/Filters/AdminInvoiceFilter.cs`** — NEW sealed record:
  ```csharp
  public sealed record AdminInvoiceFilter(
      InvoiceType? Type,
      string? CountryCode,
      string? Recipient,
      DateTimeOffset? DateRangeStart,
      DateTimeOffset? DateRangeEnd);
  ```
  `Recipient` matches case-insensitively against the invoice's recipient name. `DateRangeStart`/`End` compare `>=`/`<=` against `CreatedAt`.
- **`Core.AppServices/Features/Admin/Filters/AdminAuditLogFilter.cs`** — NEW sealed record:
  ```csharp
  public sealed record AdminAuditLogFilter(
      string? AdminUserId,
      string? ActionCode,
      string? TargetEntity,
      DateTimeOffset? DateRangeStart,
      DateTimeOffset? DateRangeEnd);
  ```
- **`Core.AppServices/Features/Admin/DTOs/AdminOrderListItemDto.cs`** — NEW sealed record:
  ```csharp
  public sealed record AdminOrderListItemDto(
      string OrderId, string OrderNumber, OrderState State,
      string CountryCode, long TotalAmountMinor, string Currency,
      DateTimeOffset CreatedAt, string MakerId, string MakerName,
      string CustomerEmail, string? ProductTitle, bool IsActive);
  ```
  Privileged admin shape — carries `CustomerEmail` + `MakerName` (no GDPR redaction; admin is privileged). `IsActive` surfaces soft-deleted rows so the frontend can tag anonymised/deactivated orders (T-0110 reconciliation).
- **`Core.AppServices/Features/Admin/DTOs/AdminInvoiceListItemDto.cs`** — NEW sealed record:
  ```csharp
  public sealed record AdminInvoiceListItemDto(
      string InvoiceId, string InvoiceNumber, InvoiceType Type,
      string CountryCode, string RecipientName, long TotalMinor,
      string Currency, DateTimeOffset CreatedAt, string? OrderId,
      string? PayoutBatchId);
  ```
- **`Core.AppServices/Features/Admin/DTOs/AdminAuditLogItemDto.cs`** — NEW sealed record:
  ```csharp
  public sealed record AdminAuditLogItemDto(
      string Id, string AdminUserId, string ActionCode,
      string TargetEntity, string TargetId, string? Notes,
      string? IpAddress, DateTimeOffset CreatedAt);
  ```
  List shape omits `BeforeJson`/`AfterJson` (the side-by-side diff is a detail-page concern, US-admin-0015 AC-2, out of scope here — keeps the list payload flat).
- **`Core.AppServices/Features/Admin/GetAllOrders.cs`** — NEW one-file feature:
  - `Query(int Page, int PageSize, OrderState? State, string? CountryCode, string? MakerId, string? CustomerEmail) : IRequest<BusinessResult<GetAllOrdersResponse>>`.
  - `GetAllOrdersResponse(PagedData<AdminOrderListItemDto> Orders)` — globally-unique name.
  - `Validator`: `Page` `GreaterThanOrEqualTo(1)`; `PageSize` `InclusiveBetween(1, 50)`; `State` `IsInEnum()` when set; `CountryCode` `Length(2)` when set (ISO-3166-1 alpha-2).
  - `Handler(IAdminQueries adminQueries)` primary-constructor DI. Steps (read-only, NO `SaveChangesAsync()`): build `AdminOrderFilter` → `adminQueries.GetAllOrdersPagedAsync(...)` → `BusinessResult.Success(new GetAllOrdersResponse(page))`.
- **`Core.AppServices/Features/Admin/GetAllInvoices.cs`** — NEW one-file feature (same shape):
  - `Query(int Page, int PageSize, InvoiceType? Type, string? CountryCode, string? Recipient, DateTimeOffset? DateFrom, DateTimeOffset? DateTo)`.
  - `GetAllInvoicesResponse(PagedData<AdminInvoiceListItemDto> Invoices)`.
  - `Validator`: page/pageSize clamps; `Type` `IsInEnum()` when set; `CountryCode` `Length(2)` when set; when both dates set, `DateFrom <= DateTo`.
- **`Core.AppServices/Features/Admin/GetAdminAuditLog.cs`** — NEW one-file feature (same shape):
  - `Query(int Page, int PageSize, string? AdminUserId, string? ActionCode, string? TargetEntity, DateTimeOffset? DateFrom, DateTimeOffset? DateTo)`.
  - `GetAdminAuditLogResponse(PagedData<AdminAuditLogItemDto> Entries)`.
  - `Validator`: page/pageSize clamps; inverted date-range check.

### Infrastructure / Database layer

- **`Infra.Database/Admin/AdminQueries.cs`** — NEW file implementing all three methods:
  - Primary-constructor DI: `AdminQueries(MakablesDbContext db, IOrderRepository orders, IInvoiceRepository invoices) : IAdminQueries`. (Composes over the repositories' `Unscoped()` queryables — the documented admin escape hatch — rather than touching `DbSet` directly, keeping the "admin reads everything" intent self-documenting.)
  - `GetAllOrdersPagedAsync`:
    1. `var baseQuery = orders.Unscoped().AsNoTracking().IgnoreAutoIncludes()`
       `    // IgnoreQueryFilters: admin reconciliation view MUST surface soft-deleted +`
       `    // anonymised orders (T-0110 GDPR hard-delete leaves anonymised rows visible).`
       `    .IgnoreQueryFilters();`
    2. Apply `filter.State` / `filter.CountryCode` / `filter.MakerId` conditionally; `filter.CustomerEmail` via `EF.Functions.ILike` (case-insensitive contains) against the snapshot contact email column.
    3. `OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)` (stable pagination).
    4. `var totalCount = await baseQuery.CountAsync(ct);` then `Skip/Take` + `Select` to `AdminOrderListItemDto` (LEFT JOIN to maker for `MakerName`; LEFT JOIN to product for `ProductTitle` when `ProductId != null`; `CustomerEmail` from the order snapshot column; `IsActive` from the entity).
    5. `return new PagedData<AdminOrderListItemDto>(items, page, pageSize, totalCount);`
  - `GetAllInvoicesPagedAsync`: `invoices.Unscoped().AsNoTracking().IgnoreAutoIncludes()` (NO `IgnoreQueryFilters` — invoices are not soft-deleted post-issuance; the AC doesn't need deleted rows). Apply `Type` / `CountryCode` / `Recipient` (`ILike`) / date-range conditionally; `OrderByDescending(CreatedAt).ThenByDescending(Id)`; two round-trips; project to `AdminInvoiceListItemDto`.
  - `GetAdminAuditLogPagedAsync`: `db.Set<AdminAuditLogEntry>().AsNoTracking()` (the entity is append-only, not `Auditable` — no global filter to ignore). Apply `AdminUserId` / `ActionCode` / `TargetEntity` (exact match) / date-range conditionally; `OrderByDescending(CreatedAt).ThenByDescending(Id)`; two round-trips; project to `AdminAuditLogItemDto` (omits `BeforeJson`/`AfterJson`).
- **`Config/Extensions/AddMakablesInfrastructure.cs`** — register `services.AddScoped<IAdminQueries, AdminQueries>();`.

### Web.Admin host

- **`Web.Admin/Controllers/AdminQueriesController.cs`** — NEW controller (mirrors the T-0105 `OrdersController` convention: `[Authorize]` under the admin audience, one-liner Mediator dispatch). Three GET actions on dedicated routes (cleanest routes — these are admin cross-tenant views, distinct from the owner-scoped `/orders` route on the other hosts):
  - `[HttpGet]` `[Route("api/v{version:apiVersion}/admin-orders")]` `ListOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] OrderState? state = null, [FromQuery] string? country = null, [FromQuery] string? makerId = null, [FromQuery] string? customerEmail = null, CancellationToken ct = default)` → `GetAllOrders.Query`.
  - `[HttpGet]` `[Route("api/v{version:apiVersion}/admin-invoices")]` `ListInvoices(... type, country, recipient, dateFrom, dateTo ...)` → `GetAllInvoices.Query`.
  - `[HttpGet]` `[Route("api/v{version:apiVersion}/audit-log")]` `ListAuditLog(... adminUserId, actionCode, targetEntity, dateFrom, dateTo ...)` → `GetAdminAuditLog.Query`.
  - Each: `[Authorize]` (admin scheme); `[ProducesResponseType(typeof(GetXxxResponse), StatusCodes.Status200OK)]` + 400/401; one-liner `HandleResult(await Mediator.Send(...))`.

### Tests

#### AdminQueriesHandlerTests (NEW, ~10 unit tests)

`backend/src/Makables.Tests/AppServices/Features/Admin/AdminQueriesHandlerTests.cs` — NSubstitute mock (`IAdminQueries`). Covers the three handlers' filter pass-through + paging + the validators.

1. **GetAllOrders_happy_path_passes_default_filter** — no filter params; `IAdminQueries.GetAllOrdersPagedAsync` called once with `AdminOrderFilter(null, null, null, null)`, page 1, size 20; response wraps the paged data.
2. **GetAllOrders_filter_by_state_and_country_passes_through** — `State = Paid`, `CountryCode = "CZ"`; assert the filter record carries both.
3. **GetAllOrders_filter_by_maker_and_customerEmail_passes_through** — `MakerId`, `CustomerEmail` set; assert pass-through.
4. **GetAllOrders_validator_rejects_pageSize_above_50** — `PageSize = 51`; `Validate().IsValid == false` on `PageSize`.
5. **GetAllInvoices_filter_by_type_and_dateRange_passes_through** — `Type = Fee`, both dates set; assert `AdminInvoiceFilter` carries them.
6. **GetAllInvoices_filter_by_recipient_passes_through** — `Recipient` set; assert pass-through.
7. **GetAllInvoices_validator_rejects_inverted_date_range** — `DateFrom > DateTo`; validation failure.
8. **GetAdminAuditLog_filter_by_adminUser_and_actionCode_passes_through** — both set; assert `AdminAuditLogFilter` carries them.
9. **GetAdminAuditLog_filter_by_targetEntity_and_dateRange_passes_through** — `TargetEntity = "Order"`, both dates; assert pass-through.
10. **GetAdminAuditLog_validator_rejects_page_below_1** — `Page = 0`; validation failure on `Page`.

#### AdminQueriesIntegrationTests (NEW, ~4 integration tests)

`backend/src/Makables.IntegrationTests/Admin/AdminQueriesIntegrationTests.cs` — Testcontainers Postgres + `WebApplicationFactory` + admin-JWT fixture + seeded cross-tenant fixtures.

1. **GET_admin_orders_returns_cross_tenant_rows** — seed orders for customer A + customer B under makers X + Y. GET `/api/v1/admin-orders` as admin. Assert 200; `totalCount` == all seeded; rows from BOTH customers + BOTH makers present; each carries `CustomerEmail` + `MakerName` (privileged view); sorted `CreatedAt DESC`. **Unscoped() proof — the maker-scoped T-0081 query would never return cross-maker rows.**
2. **GET_admin_orders_surfaces_soft_deleted_rows** — seed one active order + one soft-deleted (anonymised, mimics T-0110) order. GET `/api/v1/admin-orders`. Assert both appear; the soft-deleted row has `IsActive == false`. **`.IgnoreQueryFilters()` proof.**
3. **GET_admin_invoices_filters_by_type_and_excludes_soft_deleted** — seed Customer + Fee invoices across two countries. GET `?type=Fee&country=CZ`. Assert only CZ Fee invoices returned; `totalCount` reflects the filtered count.
4. **GET_audit_log_filters_by_actionCode_and_dateRange** — seed 3 audit entries (mixed action codes + timestamps). GET `?actionCode=...&dateFrom=...&dateTo=...`. Assert only the matching entry returned; sorted `CreatedAt DESC`; the list DTO carries `AdminUserId`/`ActionCode`/`TargetEntity`/`Notes` but NOT `BeforeJson`/`AfterJson`.

### Docs

- **`docs/architecture/roles/admin-audit-log-entry.md`** — note the read-side `IAdminQueries.GetAdminAuditLogPagedAsync` projection (list omits before/after JSONB; detail is T-0118).
- **`docs/architecture/roles/order.md`** + **`docs/architecture/roles/invoice.md`** — note the admin cross-tenant read seam (`IAdminQueries`, composes over `Unscoped()`).
- **`docs/tickets/INDEX.md`** — PM flips T-0111 to `**done**` post-merge.

### NSwag regen

Three new endpoints (`GET /api/v1/admin-orders`, `/admin-invoices`, `/audit-log`) are a contract change → **NSwag regen REQUIRED in the same PR** (admin host client only). The new `GetAllOrdersResponse` / `GetAllInvoicesResponse` / `GetAdminAuditLogResponse` + the three list-item DTOs appear in the generated admin client. Pre-commit hook (T-0013) blocks manual edits to `frontend/src/lib/api-client/`.

## Alternatives Considered

- **Option A — One shared `AdminFilter` record + one generic `GetAdminList<T>` query.** *Rejected per A.1* — the three filter sets (orders: state/country/maker/customerEmail; invoices: type/country/recipient/date; audit: adminUser/actionCode/targetEntity/date) share almost nothing. A union record forces every query to carry + ignore fields it doesn't use, and a generic query can't express the per-surface joins (maker name, product title, recipient). Three explicit one-file features are clearer and evolve independently.
- **Option B — Add free-text search across orders/invoices at MVP.** *Rejected per A.1* — no admin story names text search; it needs indexed-search infra (pg_trgm GIN + migration). Same rejection as T-0080 A.2. The AC-named filters + customerEmail/recipient `ILike` cover the admin navigation flows. Re-evaluate post-MVP.
- **Option C — Extend `IOrderRepository` / `IInvoiceRepository` with the admin read methods instead of a new `IAdminQueries`.** *Rejected per ADR 0023 + C.1* — those interfaces are write/CRUD-scoped; admin projection reads belong on the read-side seam. Mirrors the T-0080 `IOrderQueries` split. A dedicated `IAdminQueries` also makes the "admin reads everything" intent reviewable in one place.
- **Option D — Audit the read endpoints (write an `AdminAuditLogEntry` per list view).** *Rejected per ADR 0014* — ADR 0014 audits admin **writes**, not reads. Auditing every "admin viewed the order list" would flood the audit table and confuse the activity feed (US-admin-0002 / US-admin-0015 surface real actions, not page views). Reads carry no `IAdminAuditableCommand`.
- **Option E — Single SQL round-trip via `COUNT(*) OVER ()`.** *Rejected per C* — same T-0080 G rejection: Postgres can't optimise the window-function count to a cheap COUNT; it scans the full result set. Two round-trips (`CountAsync` + `Skip/Take`) let each query plan optimise independently.
- **Option F — Eager-include navigations (`.Include(o => o.Maker)`).** *Rejected per C* — materialises full entity rows; the `Select` projection lists only the DTO's columns, translating to a tight `SELECT a, b, c FROM orders LEFT JOIN makers ...`. `IgnoreAutoIncludes()` pins it.
- **Option G — Carry `BeforeJson`/`AfterJson` in the audit-log LIST DTO.** *Rejected per C* — the side-by-side diff is a detail-page concern (US-admin-0015 AC-2, out of scope). Shipping the JSONB blobs in every list row bloats the payload and leaks sensitive before/after values into a view that doesn't render them. The list carries `Notes` + metadata; the detail page (T-0118) fetches the full row.
- **Option H — Apply `.IgnoreQueryFilters()` on all three queries uniformly.** *Rejected per C* — only the admin-orders view needs soft-deleted/anonymised rows (T-0110 reconciliation). Invoices are never soft-deleted post-issuance, and the audit entity has no soft-delete filter. Applying it everywhere would be cargo-culting; the single commented call on admin-orders documents exactly why it's there.
- **Option I — Redact `CustomerEmail` from the admin order DTO (mirror the maker T-0081 surface).** *Rejected per A.2 + US-admin-0009 AC-1* — admin is the privileged actor; the story explicitly names "customer email" as a filter, which presupposes admin can see it. Redaction here would break the filter and contradict the story. This is a deliberate, story-locked divergence from the maker surface (which redacts per GDPR data minimisation).
- **Option J — Route the three endpoints under `/admin/orders`, `/admin/invoices`, `/admin/audit-log` (nested).** *Rejected (PM)* — every endpoint on the admin host is already admin-scoped (host audience), so an `/admin/` route prefix is redundant. Flat resource routes (`/admin-orders`, `/admin-invoices`, `/audit-log`) disambiguate the admin cross-tenant view from the owner-scoped `/orders` route on the other hosts without a redundant segment.

## Out of scope

- **Order detail page + audit-trail tab** — US-admin-0009 AC-2 is a frontend concern (T-0118). The order detail aggregate is already loadable via `IOrderRepository.GetByIdUnscopedAsync` (T-0105/T-0107). T-0111 ships only the list.
- **Invoice PDF download** — US-admin-0012 AC-2. Already covered by the T-0102b admin CSV / T-0088 invoice-PDF streaming precedent; the admin invoice-download endpoint surfaces downstream, not here.
- **Invoice numbering gap warning** — US-admin-0012 AC-3 (should never fire; `InvoiceNumbering` is gap-free by design). A frontend/diagnostic concern, not a list-query concern.
- **Before/after JSONB side-by-side diff** — US-admin-0015 AC-2. The list DTO omits the blobs; the detail render is T-0118.
- **Sort options beyond `CreatedAt DESC`** — Q-E (no speculative dimensions). One canonical sort at MVP; a `*Sort` enum appends later if usage warrants.
- **Free-text / OrderNumber / InvoiceNumber search** — rejected per A.1/B. The AC-named filters cover MVP.
- **The three mutations** — T-0109 (outbox retry/ack), T-0108 (country-config provider change), T-0110 (GDPR hard-delete) ship in the same PR but as separate tickets. T-0111 is the read-only harness.
- **Rate limiting on read endpoints (Q-0011)** — TOUCHED not closed; standalone secops follow-up for Gate 3. No scope expansion here.
- **New `BusinessErrorMessage` codes** — none (read-only; only Validator clamps apply).

## Acceptance criteria

- **AC-1** Given orders exist for multiple customers across multiple makers, when `GET /api/v1/admin-orders` is called with a valid admin JWT and no filters, then the response is `200 OK` with a `PagedData<AdminOrderListItemDto>` containing rows from **every** maker and customer (cross-tenant), sorted `CreatedAt DESC`, `page: 1, pageSize: 20`. **Unscoped — the maker/customer-scoped queries would never return cross-tenant rows.**
- **AC-2** Given the same data, when called with `?state=Paid&country=CZ&makerId=<X>&customerEmail=jana`, then only orders matching ALL set filters are returned (`customerEmail` matches case-insensitively); `totalCount` reflects the filtered count.
- **AC-3** Given each admin order row, when inspected, then it carries `CustomerEmail` (non-empty — privileged admin view, no GDPR redaction) and `MakerName`, plus `OrderId`, `OrderNumber`, `State`, `CountryCode`, `TotalAmountMinor`, `Currency`, `CreatedAt`, `MakerId`, `ProductTitle` (nullable for custom orders), `IsActive`.
- **AC-4** Given a soft-deleted/anonymised order exists (mimics T-0110 erasure), when `GET /api/v1/admin-orders` is called, then it appears in the result with `IsActive == false`. **`.IgnoreQueryFilters()` on the admin-orders query — admin reconciliation sees deactivated/anonymised rows.**
- **AC-5** Given Customer + Fee invoices exist across countries, when `GET /api/v1/admin-invoices?type=Fee&country=CZ` is called with an admin JWT, then only CZ Fee invoices are returned, sorted `CreatedAt DESC`; each row carries `InvoiceNumber`, `Type`, `CountryCode`, `RecipientName`, `TotalMinor`, `Currency`, `CreatedAt`, and the nullable `OrderId`/`PayoutBatchId` links.
- **AC-6** Given invoices exist, when called with `?recipient=jvm&dateFrom=2026-01-01&dateTo=2026-06-01`, then only invoices whose recipient matches `jvm` (case-insensitive) AND whose `CreatedAt` is in the inclusive range are returned.
- **AC-7** Given audit entries exist, when `GET /api/v1/audit-log` is called with an admin JWT and no filters, then a `PagedData<AdminAuditLogItemDto>` is returned sorted `CreatedAt DESC`; each row carries `AdminUserId`, `ActionCode`, `TargetEntity`, `TargetId`, `Notes`, `IpAddress`, `CreatedAt` and **does NOT carry `BeforeJson`/`AfterJson`** (those are detail-page only).
- **AC-8** Given audit entries with mixed action codes + timestamps, when called with `?adminUserId=<id>&actionCode=<code>&targetEntity=Order&dateFrom=...&dateTo=...`, then only entries matching ALL set filters are returned.
- **AC-9** Given an anonymous request OR a customer/maker JWT (`aud != admin`), when any of the three endpoints is called, then the response is `401` / `403` — admin-audience enforcement per ADR 0013. **No unscoped read is reachable from a non-admin host.**
- **AC-10** Given a request with `page=0` OR `pageSize=51` OR an inverted date range (`dateFrom > dateTo`), when any endpoint is called, then the response is `400` with a FluentValidation error pointing at the offending field. No new `BusinessErrorMessage` code (read-only surface).
- **AC-11** Build clean. Unit tests: baseline (bundle running total) + ~10 new (`AdminQueriesHandlerTests`). Integration tests: baseline + ~4 new (`AdminQueriesIntegrationTests`, incl. the Unscoped cross-tenant proof + soft-deleted visibility). `node scripts/check-consistency.mjs` exit 0. NSwag regen committed in the same PR (admin host); the generated client types all three endpoints + responses. No manual edits to `frontend/src/lib/api-client/`.
- **AC-12** Given the EF projection runs, when inspected via SQL log, then exactly two SQL statements execute per call (one `COUNT`, one `... LIMIT @pageSize OFFSET @offset`); `AsNoTracking()` + `IgnoreAutoIncludes()` confirmed on every query; `.IgnoreQueryFilters()` present ONLY on `GetAllOrdersPagedAsync` (commented) and absent from the invoice + audit queries.

## Technical notes

### Why `Unscoped()` here inverts the T-0080 IDOR shield

T-0080's customer list bakes `o.CustomerUserId == customerId` into the query — the predicate IS the IDOR shield, so a customer can never select another customer's row. The admin surface is the exact inverse: admin is the privileged actor (ADR 0013 §"the Unscoped escape hatch is admin-host only"), so the query has **no** owner predicate. The boundary moves from the SQL WHERE clause to the **host audience** — only a JWT with `aud=admin` reaches these endpoints. That is why `security_touching: true`: every reviewer pass must confirm `IOrderRepository.Unscoped()` / `IInvoiceRepository.Unscoped()` are reachable only from `Web.Admin`. The Reviewer rejects any `Unscoped()` call from `Web.Customer` / `Web.Maker` / `Web.Public` (the documented webhook exception aside).

### Why the admin order DTO carries `CustomerEmail` (privileged, no redaction)

The maker-facing T-0081 list omits customer email per GDPR data minimisation (the maker has no need-to-know the customer's email — the order-message thread is the channel). Admin does have need-to-know: US-admin-0009 AC-1 names "customer email" as a filter dimension, which presupposes admin can see and search it. The admin surface is the one place the full contact snapshot is visible. After a T-0110 erasure, the snapshot reads "Anonymized" (the anonymisation already ran at the data layer), so the privileged view still respects erasure — it shows whatever the row currently holds.

### Why `.IgnoreQueryFilters()` only on admin-orders (one commented call)

The global `Auditable` soft-delete filter hides deactivated rows by default. Admin order reconciliation legitimately needs to see soft-deleted + anonymised orders — after a T-0110 GDPR hard-delete of the user, the order rows remain (anonymised), and admin must still see them to confirm the erasure ran and to reconcile payouts. So `GetAllOrdersPagedAsync` opts out of the filter with a single commented `.IgnoreQueryFilters()`. Invoices are never soft-deleted post-issuance (role/invoice.md), so `GetAllInvoicesPagedAsync` keeps the filter. `AdminAuditLogEntry` is not `Auditable` (it's append-only with a DB trigger blocking UPDATE/DELETE), so there's no filter to ignore. Applying `IgnoreQueryFilters()` uniformly would be cargo-culting.

### Why two round-trips (not `COUNT(*) OVER ()`)

Same as T-0080: Postgres can't optimise the window-function count to a cheap COUNT — it scans the full result set. `CountAsync` + `Skip/Take` projection lets each query plan optimise independently (the COUNT uses the filtered index; the projection uses the `(created_at DESC)` ordering index). Both are sub-millisecond at MVP scale (2 admins, low query volume).

### Why no audit row for reads

ADR 0014 audits admin **writes** — state transitions, config edits, deletions — because those are the recordable "who did what to whom" actions. A read (viewing the order list) is not a recordable action; auditing it would flood the audit table and pollute the activity feed (US-admin-0002 / US-admin-0015 surface real actions). The three query handlers carry **no** `IAdminAuditableCommand` and run only the `ValidationPipelineBehavior` (not `UnitOfWorkPipelineBehavior`). This is the read-only counterpart to the bundle's three mutations, which DO implement `IAdminAuditableCommand`.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Admin/IAdminQueries.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/Filters/AdminOrderFilter.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/Filters/AdminInvoiceFilter.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/Filters/AdminAuditLogFilter.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/DTOs/AdminOrderListItemDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/DTOs/AdminInvoiceListItemDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/DTOs/AdminAuditLogItemDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/GetAllOrders.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/GetAllInvoices.cs`
- `backend/src/Makables.Core.AppServices/Features/Admin/GetAdminAuditLog.cs`
- `backend/src/Makables.Infra.Database/Admin/AdminQueries.cs`
- `backend/src/Makables.Web.Admin/Controllers/AdminQueriesController.cs`
- `backend/src/Makables.Tests/AppServices/Features/Admin/AdminQueriesHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Admin/AdminQueriesIntegrationTests.cs`

### Modified
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IAdminQueries`.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (admin host); committed in the same PR.
- `docs/architecture/roles/admin-audit-log-entry.md`, `docs/architecture/roles/order.md`, `docs/architecture/roles/invoice.md` — note the admin read seam.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0111.md`.

## Status log

- 2026-06-14 `draft` by BA. Created as the first ticket in the admin-tooling bundle (T-0111 read queries → T-0109 outbox retry/ack → T-0108 country-config → T-0110 GDPR hard-delete), one PR in risk-ascending order. T-0111 is the read-only verification harness the three mutations assert their side effects against. Slice scope: new `IAdminQueries` interface + three filter records + three list-item DTOs + three one-file query features + `AdminQueries` EF impl (composes over `Unscoped()`) + new admin controller (3 GET endpoints) + ~10 unit tests + ~4 integration tests (incl. Unscoped cross-tenant proof + soft-deleted visibility). No new error codes, migrations, outbox events, email, or i18n keys.
- 2026-06-14 `draft → ready` by BA. User-locked at the bundle deliberation: **Q-E** filter set = exactly the AC-named dimensions, no more (orders: state/country/maker/customerEmail per US-admin-0009; invoices: type/country/recipient/date per US-admin-0012; audit: adminUser/actionCode/targetEntity/date per US-admin-0015). PM-absorbed: `IAdminQueries` read-side split (ADR 0023), `Unscoped()` from `Web.Admin` only (ADR 0013), `.IgnoreQueryFilters()` only on admin-orders (commented), no audit row for reads (ADR 0014 — reads are not auditable), page-based clamp [1,50], `CreatedAt DESC`, globally-unique Response names, NSwag admin regen. `security_touching: true` (unscoped admin reads + privileged `CustomerEmail` exposure). **Q-0011** TOUCHED not closed — admin-JWT-gated, standalone secops follow-up for Gate 3. No `manual_steps`. **Ready for dotnet-backend.** Implemented as the first slice of the four-ticket bundle in one branch/PR.
