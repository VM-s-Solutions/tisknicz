# Gate 3 (Security) — T-0126 admin invoice-PDF download + overview count reads

- **Branch:** `feat/admin-reads-followups` (4 commits)
- **security_touching:** YES
- **Scope:** `GET /api/v1/admin-invoices/{invoiceId}/pdf` (Unscoped PII stream), `GET /api/v1/payout-batches/count`, `GET /api/v1/outbox-events/stalled/count`
- **Verdict: GATE3_PASS**

---

## 1. Invoice download — audience is the ONLY authorization (headline)

The endpoint streams ANY invoice PDF by id (customer recipient name, address, tax ids, line items). There is no owner predicate — by design, admin is privileged. The audience gate is therefore the entire authorization story, so it was verified exhaustively.

- `[Authorize]` present on `AdminInvoicesController` (controller-level, line 37). No `[AllowAnonymous]` anywhere.
- Admin host wiring: `Web.Admin/Program.cs` calls `AddMakablesAuth(config, MakablesHosts.Admin)`.
- `AcceptedAudiencesFor(MakablesHosts.Admin) => [MakablesAudiences.Admin]` — admin host accepts ONLY `aud=admin`. Unlike Customer/Maker hosts, it does NOT additionally accept any other audience. `ValidateAudience = true`, `ValidAudiences = ["admin"]`, `ValidateIssuerSigningKey`, `ValidAlgorithms = [HmacSha256]`, `ValidateLifetime`, 30s skew.
- Unscoped read uses `GetByIdUnscopedReadOnlyAsync` (AsNoTracking + IgnoreQueryFilters) — admin sees soft-deleted/anonymised rows for reconciliation; no owner column, as intended.

### Cross-audience-401 proof assessment — PASS (real non-admin token, not missing-token)

`AdminInvoiceDownloadIntegrationTests.GET_admin_invoice_with_customer_or_maker_jwt_is_401` is the actual risk case, not a substitute. The test mints tokens via the **real `JwtIssuer`** with the **same signing key and same issuer** as the host, differing ONLY in `aud`:

- customer JWT (`aud=customer`, role Customer) → 401
- maker JWT (`aud=maker`, role Maker) → 401

This is a genuine forged-but-correctly-signed cross-audience replay (the threat), not merely an unauthenticated/missing-token probe. The audience binding — not signature or presence — is what produces the 401. Happy-path admin (`aud=admin`) returns 200 byte-equal with the PDF. The three actors (customer JWT, maker JWT, and — covered by the bare `[Authorize]` + middleware — unauth) never receive the PDF.

## 2. PII cache hygiene — PASS

- 200 sets `Cache-Control: private, no-store` (no shared-proxy caching of recipient PII). Verified in both unit (`AdminInvoiceDownloadTests`, exact string) and integration (`Contain("no-store")`).
- ETag set only on success; conditional `If-None-Match` → 304 re-auths through `[Authorize]` (revalidation is safe). Range processing disabled (zero-benefit attack surface).
- 404 path (`InvoiceNotYetRendered`) carries NO `Cache-Control` (unit test asserts `CacheControl.ToString().Should().BeEmpty()` on blob-miss) and NO PII — generic body identical for no-row / null-PdfBlobPath / blob-purged-race. No enumeration oracle beyond exist/not-exist, which is acceptable for a privileged admin actor.

## 3. Blob path safety — PASS

`invoice.PdfBlobPath` is written only by the T-0068b issuer (deterministic `cz/orders/{orderId}/{number}.pdf`) and streamed verbatim to `blobs.DownloadAsync(BlobContainer.Invoices, path, ct)`. The `{invoiceId}` route value is a lookup key in a parameterized EF predicate (`i => i.Id == invoiceId`), never a path component. No user input reaches the blob path; no path construction in the controller.

## 4. Count endpoints — PASS

Read-only integer aggregates, no PII in the response (`{ count }`). `[Authorize]` admin-audience on both controllers. `state` binds to the `PayoutBatchState` enum and is `IsInEnum()`-validated (no injection; out-of-range → 400). EF `CountAsync` is parameterized. Cross-audience rejection pinned by `Count_endpoints_reject_non_admin_jwt` (real customer + maker tokens → 401).

## 5. No new bypass surface — PASS

No new unauthenticated path. All three endpoints inherit the admin host's existing audience gate; no `[AllowAnonymous]`, no minimal-API map bypassing `[Authorize]`.

---

## Folds (non-blocking)

- **F1 (posture, audit):** An admin invoice DOWNLOAD streams customer PII but leaves NO audit trail (the count handlers correctly note "reads are not audited"; the download is controller-direct so it never touches `IAdminAuditableCommand`). T-0110 erasure IS audited; a privileged PII read arguably should leave a trace for the weekly-checkpoint accountability model. Not a Gate-3 blocker (admin is trusted + the host is the control), but recommend a Q-item: "should admin invoice-PDF reads emit an audit row?" Track in `docs/questions/open.md`.

No BLOCK items. Audience enforcement, PII cache policy, blob-path safety, and injection posture all pass with real cross-audience test coverage.
