# Gate 3 (Security) — order-dashboards bundle

- **Date:** 2026-06-12
- **Branch:** `feat/order-cleanup-bundle` (7 commits over master: T-0088, T-0089, NSwag regen, T-0086a, T-0086b, T-0087a, T-0087b)
- **Security-touching ticket:** T-0088 (PII file streaming + IDOR)
- **Verdict:** `FOLD` — gate passes; one Medium policy finding folded as a mandatory follow-up (no code change required in this bundle to merge).

## 1. Invoice download IDOR (headline) — SOLID

- **Customer host** (`backend/src/Makables.Web.Customer/Controllers/OrdersController.cs`, `DownloadInvoice`): session → `GetByIdForCustomerReadOnlyAsync` → unscoped invoice lookup → blob. Ownership predicate `o.Id == orderId && o.CustomerUserId == customerUserId` (`backend/src/Makables.Infra.Database/Orders/OrderRepository.cs:114`).
- **Maker host** (`backend/src/Makables.Web.Maker/Controllers/OrdersController.cs`, `DownloadInvoice`): session → `makers.GetByUserIdAsync` (null → 404 before any order lookup, AC-6) → `GetByIdForMakerReadOnlyAsync` with predicate `o.Id == orderId && o.MakerId == makerId` (`OrderRepository.cs:93`).
- **Ownership-before-blob on every path.** The unscoped `IInvoiceRepository.GetByOrderIdAsync` runs only after the scoped order load; `IBlobStorageClient.DownloadAsync` runs only after both.
- **No enumeration oracle.** Cross-tenant and unknown ids both return the identical 404 `order.notFound` shape; only the verified owner can distinguish `invoice.notYetRendered`. Integration-pinned in `backend/src/Makables.IntegrationTests/Orders/OrderInvoiceDownloadTests.cs` (`GET_invoice_404_paths_are_oracle_free`).
- **Denied paths pin `Received(0)`** on the invoice repo and blob client: `Makables.Tests/Web/Customer/Controllers/OrdersControllerInvoiceDownloadTests.cs` (lines 94–118, 145), `Makables.Tests/Web/Maker/Controllers/OrdersControllerInvoiceDownloadTests.cs` (lines 106–132).

## 2. Cache headers on PII — PASS

- `Cache-Control: private, no-store` + ETag/304 set only after blob success; no cache headers on any 404 (maker unit test pins empty `CacheControl` on the blob-purged 404, line 158). Integration test asserts `private, no-store` on both hosts' 200s.
- `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"` runs through `EscapeFilenameForHeader` (backslash + quote escaping); `InvoiceNumber` is platform-generated `FV-CZ-NNNNNNNN`; ASP.NET Core header validation rejects CR/LF — header-injection safe.
- Range processing disabled on both hosts (small platform artifact; reduced surface).

## 3. [Authorize] + audience — PASS

- Both `OrdersController`s carry class-level `[Authorize]`; new actions inherit it. Per-host `ValidAudiences` in `backend/src/Makables.Config/Extensions/AddMakablesAuth.cs:128-131` — a customer JWT cannot be replayed against the maker host.
- All 4 new frontend routes fetch exclusively via `lib/api-client-helpers/orders-client.ts` (audience `customer`) / `maker-orders.ts` (audience `maker`) through `apiFetch` with audience-scoped cookies.

## 4. Blob path traversal — PASS

- `Invoice.PdfBlobPath` streamed verbatim; only production writer is `IssueInvoice.cs:240` via deterministic `BuildBlobPath` (`{cc}/orders/{orderId}/{invoiceNumber}.pdf` — all three components platform-generated, zero user input). `AttachPdfBlobPath` is set-once with domain-test-pinned invariants (`InvoiceTests.cs`).

## 5. Maker GDPR (DOM surface) — PASS, but see Finding F-1

- Zero customer email in the maker frontend diff. Contact card renders name + phone only; `tel:` href with raw phone (safe); no `mailto:` anywhere. Tracking link uses `target="_blank" rel="noopener noreferrer"`. Maker DTOs (`IMakerOrderDetailDto`, `IMakerOrderListItemDto`) carry no email field.

## 6. Thread XSS surface — PASS

- Zero `dangerouslySetInnerHTML` / `innerHTML` in the diff. Message body rendered as a React text node (`frontend/src/components/shared/order-message-thread.tsx:264`); attachment filenames likewise. No raw HTML path exists.

## 7. Label download — PASS

- `downloadShippingLabel` in `maker-orders.ts` hits the maker-scoped T-0075 backend route through `apiFetch('maker', …, { parse: 'blob' })` — same audience-cookie mechanism as `downloadMakerOrderFile` (attachments + invoice). Consistent; no direct blob URLs anywhere.

## 8. Mark-read / post cross-tenant — PASS

- Customer thread client imports only `orders-client.ts` functions; maker thread client only `maker-orders.ts`. The shared `OrderMessageThread` takes injected callbacks and never imports a client — no way to construct a cross-audience call. Backend predicates (T-0079, verified previously) remain the shield.

## 9. Secrets / bundle — PASS

- No secrets in the diff (all matches are integration-test in-memory config stubs and fixed Argon2 test fixtures). `package.json`, lockfile, `.env*` untouched. `parse: 'blob'` addition to `api-fetch.ts` introduces no new auth or env surface.

## Findings

### F-1 (Medium, FOLD) — Maker invoice download reopens the customer-email channel via PDF content

The maker host streams the **Customer**-type invoice, and the rendered PDF embeds `RecipientEmail` (`backend/src/Makables.Infra.PdfRendering/QuestPdfInvoiceRenderer.cs:178` and `:321`). Every paid order therefore hands the maker the customer's email through a sanctioned UI button — directly contradicting the T-0081/T-0082 compile-time GDPR lock ("DTO deliberately carries no customer email"; "no mailto rendered, AC-9") that this same bundle's pages advertise and enforce at the DOM level. Note the docs conflict internally: `docs/user-stories/maker/README.md` US-maker-0010 AC-1 still grants makers "customer name + email + phone", while the shipped tickets revoked email. This is not an exploit (authenticated, ownership-scoped counterparty receiving a business document), so it does not block — but it needs an explicit PM/product decision:

1. **Accept** email-in-invoice as the sanctioned channel → update T-0081/T-0082 GDPR-lock rationale + US-maker-0010 so the policy is consistent; or
2. **Redact** — serve the maker a variant without `RecipientEmail` (renderer change or maker-specific render), keeping the lock airtight.

**Fold target:** `docs/questions/open.md` entry + follow-up ticket before launch.

### F-2 (Info) — Asymmetric OpenAPI metadata

Customer `DownloadInvoice` declares `ProducesResponseType(403)` which the action never returns; the maker action omits it. Cosmetic; zero client-shape impact (both generate `Promise<void>`).

### F-3 (Info) — ETag + `no-store` combination

Conditional GET against a `no-store` response is unusual but mirrors the established T-0064 attachment policy verbatim; revalidation always round-trips the auth check. No action.

## Verdict

**FOLD** — all nine checklist areas pass; the IDOR chain is solid and test-pinned on both hosts. Merge may proceed once F-1 is logged in `docs/questions/open.md` with a follow-up ticket reference.
