# Backlog manifest

PM owns this file. Updates on every state change.

This manifest lists every ticket as a single row with metadata. **When a ticket moves to `ready`, PM expands it to a full `T-NNNN-<slug>.md` file** using `template.md`. The full file carries the AC, scope, test plan reference, and status log.

Until expanded, the manifest row is the lightweight backlog representation. Sprint plans are at the bottom of this file.

## Legend

- **Phase**: implementation phase (1 = scaffold; 2 = identity; 3 = catalog; 4 = orders; 5 = post-order; 6 = polish)
- **Size**: S (<4h), M (4–16h), L (>16h — must split before `ready`)
- **State**: draft | ready | in_progress | in_review | qa | done | blocked
- **Owner**: `dotnet-backend` / `dotnet-db` / `frontend` / `secops` / `architect` when in_progress

---

## Phase 1 — Foundation scaffold (sequential, blocking everything)

| Ticket | Title | Phase | Size | State | Depends on | Stories | ADRs |
|---|---|---|---|---|---|---|---|
| T-0001 | Scaffold .NET solution skeleton — Core.Domain, Core.AppServices, Config, Infra.*, Web.*, Functions, Tests | 1 | M | **done** | — | — | 0001, 0007, 0008 |
| T-0002 | EF Core: MakablesDbContext, audit interceptor, global query filter for soft-delete | 1 | M | **done** | T-0001, T-0004, T-0006 | — | 0011, 0013, 0014 |
| T-0003 | Wire MediatR + FluentValidation + pipeline behaviors (Validation + UnitOfWork) | 1 | S | **done** | T-0001, T-0004, T-0002 | — | 0002 |
| T-0004 | Shared types: BusinessResult, Error, ErrorType, BusinessErrorMessage; ICommand / IQuery markers; MakablesApiController base | 1 | S | **done** | T-0001 | — | 0002 |
| T-0005 | Money value object + MoneyFormatter + tests | 1 | S | **done** | T-0001 | — | 0003 |
| T-0006 | Auditable base entity + IClock + IIdGenerator + IUserSessionProvider (interceptor wiring moved to T-0002) | 1 | S | **done** | T-0001, T-0004 | — | 0011, 0013, 0014 |
| T-0007 | NumberingSequence table + IOrderNumberGenerator / IInvoiceNumberGenerator / IPayoutBatchNumberGenerator with FOR UPDATE lock | 1 | M | **done** | T-0001, T-0002, T-0006 | — | 0009 |
| T-0008 | DI wiring: AddMakablesInfrastructure / AddMakablesAuth / AddMakablesCors / AddMakablesMediator / AddMakablesClients / AddMakablesRateLimiting | 1 | M | **done** | T-0001, T-0002, T-0003, T-0006, T-0007 | — | 0008 |
| T-0009 | Four Web hosts (Customer / Maker / Admin / Public) sharing Config; per-host Program.cs; per-host CORS + rate limit | 1 | M | **done** | T-0008 | — | 0005, 0008 |
| T-0010 | Country + CountryConfiguration entity + seed migration (CZ row); ICountryConfigurationRepository | 1 | M | **done** | T-0002, T-0006, T-0007, T-0008, T-0009 | — | 0004, 0013 |
| T-0011 | Outbox table + IOutbox producer helper + OutboxRepository; AdminAuditLogEntry table + AdminAuditPipelineBehavior | 1 | M | **done** | T-0002, T-0003, T-0006, T-0008, T-0010 | — | 0014, 0020 |
| T-0012 | API versioning wiring (Asp.Versioning.Mvc); URL-path versioning; openapi/v1.json per host | 1 | S | **done** | T-0009 | — | 0021 |
| T-0013 | NSwag pipeline: nswag/config.json; npm run generate:api; CI spec-hash parity check; pre-commit hook blocking manual edits to api-client/ | 1 | M | **done** | T-0009, T-0012 | — | 0022 |
| T-0014 | Structured logging (Serilog), OpenTelemetry traces + metrics via .AddServiceDefaults() (Aspire); Application Insights wiring | 1 | M | **done** | T-0009 | — | 0023 |
| T-0015 | Frontend scaffold: route groups (public, auth, customer, maker, admin); api-client/ folder structure; api-fetch wrapper; lib/runtime Result type; lib/i18n/cs-CZ catalog scaffold | 1 | M | **done** | T-0013 | — | 0005, 0022 |
| T-0016 | Deploy: Bicep templates for Azure (Postgres Flexible Server, four App Services, Functions, Blob, Key Vault, App Insights); staging + production environments; deploy pipeline GitHub Actions | 1 | L | **done** | T-0014 | — | 0023 |

**Phase 1 total:** 16 tickets. Sequential where dependencies dictate; T-0014 / T-0015 / T-0016 can parallelize at the end.

---

## Phase 2 — Identity (auth, users, makers)

| Ticket | Title | Phase | Size | State | Depends on | Stories | ADRs |
|---|---|---|---|---|---|---|---|
| T-0020 | User + RefreshToken entities + migrations; IUserRepository + IRefreshTokenRepository | 2 | M | draft | Phase 1 done | — | 0012, 0013 |
| T-0021 | IPasswordHasher (Argon2id); IJwtIssuer (HS256 + audience); Argon2 + JwtIssuer impls; tests | 2 | M | draft | T-0020 | — | 0012 |
| T-0022 | IAuthService + AuthService impl: Register, Login, Refresh, Logout, lockout policy | 2 | L | draft | T-0021 | US-customer-0001, 0002, 0019, 0020; US-maker-0002, 0016; US-admin-0001, 0017 | 0012 |
| T-0023 | Magic-link flow: SendMagicLinkAsync + ConsumeMagicLinkAsync; email template `magic-link/cs-CZ` | 2 | M | draft | T-0022, T-0029 | US-customer-0003 | 0012, 0019 |
| T-0024 | Email confirmation flow: SendEmailConfirmation + ConfirmEmail; email template `welcome-customer/cs-CZ`, `welcome-maker/cs-CZ`; gate order placement on confirmed email | 2 | M | draft | T-0022, T-0029 | US-customer-0005 | 0012, 0019 |
| T-0025 | Password reset flow: SendPasswordResetAsync + ConfirmPasswordResetAsync; email template `reset-password/cs-CZ` | 2 | M | draft | T-0022, T-0029 | US-customer-0006 | 0012, 0019 |
| T-0026 | Google OAuth: GoogleOAuthClient + CompleteGoogleOAuthAsync; audience-bound state; reject for admin audience | 2 | M | draft | T-0022 | US-customer-0004 | 0012 |
| T-0027 | Auth middleware per host: JWT validation, audience enforcement, role check | 2 | M | done | T-0021, T-0009 | — | 0005, 0012 |
| T-0028 | Email pipeline: SendGrid Dynamic Templates + DB-backed translation (EmailTemplate + EmailTemplateTranslation) + ILanguageResolver + outbox payload carries LanguageCode; ADR 0019 amended Resend→SendGrid; bounce webhook deferred | 2 | L | done | T-0011, T-0020, T-0023, T-0024, T-0025 | — | 0019 |
| T-0029 | Outbox processor Function: ProcessOutboxFunction (timer 30s + HTTP) routes to send-email queue → SendEmailFunction calls IEmailSendService; OutboxRetryPolicy (1m→5m→15m→1h→6h→24h, stall after 6); OutboxEvent.ParkPendingConsumer; queue-message body = bare outbox id (payload stays in Postgres) | 2 | M | done | T-0011, T-0028 | — | 0020 |
| T-0030 | Address entity + IAddressRepository + IAddressFormatValidator (reads CountryConfiguration.ZipFormat); ConfigurationDrivenAddressFormatValidator with regex cache + timeout; AddressZipRules FluentValidation mixin | 2 | S | done | T-0010 | — | 0010 |
| T-0031 | IAddressGeocoder + MapboxAddressGeocoder; backend autocomplete proxy (Customer + Maker hosts via shared Config controller); partitioned per-user rate-limit policy `addresses-autocomplete` (20/min/user, 5/min/IP for unauth); Polly retry 2x; non-blocking failure per ADR 0010 §"Geocoding policy" | 2 | M | done | T-0030 | — | 0010 |
| T-0032 | ICompanyRegistry + AresCompanyRegistry; CzechIcoValidator mod-11; company_registry_cache (24h TTL + 7-day stale fallback); IMemoryCache hot layer; shared Polly ResiliencePipelineRegistry; CzechLegalForms map | 2 | M | done | T-0030 | — | 0018 |
| T-0033 | Maker entity (Auditable; snapshot of ARES fields + IsVerified admin gate); IMakerRepository (Add + IcoExistsAsync + GetByUserIdAsync); RegisterMaker command — 6-step flow: IČO format gate → ARES lookup → dissolved-entity reject (MakerCompanyDissolved) → email/IČO conflict pre-checks → atomic User+Address+Maker add → email-confirmation token (shared issuer). Stale ARES snapshot surfaces on Response.SnapshotIsStale (non-blocking per ADR 0018). | 2 | L | done | T-0020, T-0030, T-0032 | US-maker-0001 | 0010, 0012, 0018 |
| T-0034 | UpdateMakerProfile (maker self-service: bio, bankAccount, personalPickupEnabled, pickupNote — IDOR-shielded; user resolved from session) + admin VerifyMaker + DeactivateMaker + RefreshMakerFromAres commands (all IAdminAuditableCommand → before/after JSONB via AdminAuditPipelineBehavior). Adds CzechBankAccountValidator (ČNB mod-11) + Address.Update mutator (clears coordinates so the geocoder sweep refills). | 2 | M | done | T-0033 | US-maker-0003, 0015; US-admin-0003, 0004, 0005 | 0014 |
| T-0035 | Backend AuthController (9 endpoints: register/login/logout/refresh/confirm-email/{request,confirm}-password-reset/{request,consume}-magic-link) on every host via shared Config; RegisterMakerController on Public host only; AuthCookies helper (HttpOnly+Secure+SameSite=Strict, makables_access_{aud}/makables_refresh_{aud}); IHostAudience per Web host. Frontend pages /auth/login, /auth/register, /auth/register/maker, /auth/verify, /auth/reset, /auth/magic + email-confirmation banner; lib/api-client-helpers/auth.ts hand-written wrappers; apiFetch defaults to credentials:'include'. NSwag regen deferred. | 2 | L | done | T-0024, T-0025, T-0023, T-0026, T-0033 | US-customer-0001–0006; US-maker-0001, 0002 | 0012, 0005, 0008 |
| T-0036 | Backend GetMyProfile / UpdateUserProfile / ChangePassword (Auth feature) + GetMyMakerProfile (Maker feature); ProfileController under /api/v1/me (5 endpoints, [Authorize], shared across hosts). Frontend /dashboard/zakaznik/profile (3 sections: personal info / password / logout) and /dashboard/maker/profil (read-only ARES snapshot + verification badge + stale-snapshot banner + editable bio/bankAccount/personalPickupEnabled/pickupNote). Categories + pickup-address deferred. | 2 | M | done | T-0034, T-0035 | US-customer-0018, 0019, 0020; US-maker-0003, 0015, 0016 | 0012, 0014, 0018 |

**Phase 2 total:** 17 tickets.

---

## Phase 3 — Catalog (products, browse)

| Ticket | Title | Phase | Size | State | Depends on | Stories | ADRs |
|---|---|---|---|---|---|---|---|
| T-0040 | Category entity (Auditable + Slugify NFD-decompose Czech diacritics) + ICategoryRepository (Add / GetByIdAsync / SlugExistsAsync) + partial unique index ix_categories_slug WHERE is_active + UniqueConstraintTranslator race-translation; migration seeds 6 launch categories (cat-3d-tisk, cat-klasicky-tisk, cat-potisk-textilu, cat-laser-cnc, cat-velkoformat, cat-handmade) + maker_categories join (composite PK, no domain entity); admin CreateCategory / UpdateCategory (rename without touching slug per US-admin-0013 AC-2) / DeactivateCategory (all IAdminAuditableCommand, fail-closed on missing session). | 3 | M | done | T-0010 | US-admin-0013 | 0014 |
| T-0041 | Product entity (Auditable; Money pair + PriceType Fixed/From/OnRequest; owned ProductImage collection, ≤10 cap; currency immutable) + IProductRepository; CreateProduct (tenant currency from CountryConfiguration, maker from session) / UpdateProduct / DeleteProduct (soft) / AddProductImage / RemoveProductImage (best-effort blob delete), all IDOR-shielded by maker ownership. ImageUploadValidator (≤5MB, jpeg/png/webp, magic-byte sniff). Web.Maker ProductController (CRUD + multipart upload → product-images blob) + Web.Public ProductImageController (anonymous ETag-cached streaming). | 3 | L | done | T-0040, T-0042 | US-maker-0004 | 0003, 0011 |
| T-0042 | IBlobStorageClient (`Core.Domain/Storage/`) + AzureBlobStorageClient — `BusinessResult<T>` surface, no exceptions cross the boundary; container allow-list rejects unknown names (would default to private and break public product-images); conservative path safety (no leading `/`, `\\`, `.`/`..` segments, ≤1024 chars); 404 → `BlobNotFound`, other RequestFailedException → `Blob{Upload,Download}Failed` Transient. AzureBlobStorageOptions ValidateOnStart requires `ConnectionString` (dev/CI/Azurite) OR https `ServiceUri` (Managed Identity / DefaultAzureCredential). HTTP file-streaming endpoints deferred to per-feature tickets (T-0041 product images, etc.). | 3 | M | done | T-0001 | US-customer-0017; US-maker-0009, 0013; US-admin-0012 | 0011 |
| T-0043 | Catalog query: GetPagedMakers with filters (category, city, rating) + Specification; OrderSort | 3 | M | draft | T-0033, T-0041 | US-customer-0007 | — |
| T-0044 | Maker profile query: GetMakerBySlug — bio + active products + recent reviews + rating | 3 | M | draft | T-0033, T-0041 (T-0050 reviews not yet but null-safe) | US-customer-0008 | — |
| T-0045 | Product detail query: GetProductById | 3 | S | draft | T-0041 | US-customer-0009 | — |
| T-0046 | Frontend: /katalog page (filters, list, pagination) | 3 | M | draft | T-0043 | US-customer-0007 | — |
| T-0047 | Frontend: /katalog/[slug] maker profile page | 3 | M | draft | T-0044 | US-customer-0008 | — |
| T-0048 | Frontend: /produkt/[id] product detail page | 3 | M | draft | T-0045 | US-customer-0009 | — |
| T-0049 | Frontend: /dashboard/maker/produkty (CRUD UI; image picker; price/weight forms) | 3 | L | draft | T-0041, T-0042 | US-maker-0004 | — |

**Phase 3 total:** 10 tickets.

---

## Phase 4 — Orders (placement → payment → escrow → delivery)

| Ticket | Title | Phase | Size | State | Depends on | Stories | ADRs |
|---|---|---|---|---|---|---|---|
| T-0060 | Order entity + state machine + IOrderRepository (scoped ForCustomer / ForMaker / Unscoped) | 4 | L | draft | T-0033, T-0041 | — | 0002, 0013 |
| T-0061 | OrderPricing domain service + PricingService orchestrator; reads CountryConfiguration; tests | 4 | M | draft | T-0010, T-0041 | — | 0003, 0004 |
| T-0062 | OrderNumber + IOrderNumberGenerator integration into CreateOrder | 4 | S | draft | T-0007, T-0060 | — | 0009 |
| T-0063 | CreateOrder command + Validator (extensive — see US-customer-0010 AC list) + Handler + controller; persists Order in `PendingPayment` | 4 | L | draft | T-0060, T-0061, T-0062 | US-customer-0010, 0011 | 0003, 0009, 0010 |
| T-0064 | Order attachments upload endpoint (multipart, validates type+size, stores under `order-attachments/cz/orders/<id>/`); GetAttachment streaming endpoint with ownership check | 4 | M | draft | T-0042, T-0063 | US-customer-0010; US-maker-0010 | 0011 |
| T-0065 | IPaymentProvider + ComgatePaymentProvider; IPaymentProviderFactory; CreatePayment integrated into CreateOrder flow returning Comgate redirect URL | 4 | L | draft | T-0063 | US-customer-0010 | 0016 |
| T-0066 | Comgate webhook controller (`POST /api/v1/public/webhooks/comgate`): IP allowlist + re-fetch status + idempotency; dispatches MarkOrderPaid command | 4 | M | draft | T-0065 | US-customer-0010 | 0016, 0020 |
| T-0067 | MarkOrderPaid command: transitions PendingPayment → Paid; enqueues outbox events: customer email (`order-paid`), maker email (`new-order`), invoice.generate | 4 | M | draft | T-0066, T-0011 | US-customer-0010 | 0016, 0020 |
| T-0068 | Invoice entity + IInvoiceRepository + IInvoiceNumberGenerator integration; InvoiceService.IssueAsync with InvoicingMode switch (None / StandardVat — others not implemented); QuestPDF renderer; stores PDF in Blob `invoices/cz/orders/<id>/<invoiceNumber>.pdf` | 4 | L | draft | T-0011, T-0042, T-0061 | US-customer-0010, 0017; US-admin-0012 | 0003, 0009, 0011, 0013 |
| T-0069 | GenerateInvoice Function (queue-triggered from outbox); attaches PDF to outbox customer email event | 4 | M | draft | T-0068, T-0029 | US-customer-0017 | 0020 |
| T-0070 | IShippingCarrier + PacketaShippingCarrier; IShippingCarrierFactory; widget config endpoint for frontend | 4 | M | draft | T-0010 | US-customer-0010; US-maker-0007 | 0017 |
| T-0071 | AcceptOrder command (maker action): Paid → Accepted; outbox event for customer notification | 4 | S | draft | T-0060, T-0011 | US-maker-0006 | — |
| T-0072 | ShipOrder command (Zásilkovna path): creates Packeta shipment, transitions Accepted → Shipped, sets AutoDeliverAt, outbox customer notification with tracking URL, queues GenerateLabel Function | 4 | M | draft | T-0070, T-0071 | US-maker-0007 | 0017 |
| T-0073 | ShipOrder command (personal-pickup path): no Packeta call, transitions Accepted → Shipped, sets AutoDeliverAt | 4 | S | draft | T-0071 | US-maker-0008 | — |
| T-0074 | GenerateLabel Function (queue-triggered): fetches Packeta label, stores in Blob `invoices` container under labels path | 4 | M | draft | T-0070, T-0042 | US-maker-0009 | 0017 |
| T-0075 | Label download endpoint (`GET /api/v1/maker/files/orders/<id>/label`): cache lookup → Packeta fallback; ownership check | 4 | S | draft | T-0074 | US-maker-0009 | 0011, 0017 |
| T-0076 | MarkOrderDelivered command (customer or auto or carrier-sourced); transitions Shipped → Delivered, sets DeliveredAt; outbox events | 4 | S | draft | T-0072, T-0073 | US-customer-0013 | — |
| T-0077 | AutoDeliverOrders Function (timer daily 08:00 UTC) | 4 | S | draft | T-0076 | US-customer-0013 | 0020 |
| T-0078 | SyncShipmentStatuses Function (timer every 6h): pull Packeta status; carrier-confirmed delivery transitions; raise dispute for `Returned`/`Failed` | 4 | M | draft | T-0070, T-0076 | US-customer-0013 | 0017, 0020 |
| T-0079 | OrderMessage entity + IOrderMessageRepository; SendMessage command (customer/maker); debounced notification outbox event (5-min digest); read-only viewing for admin | 4 | M | draft | T-0060, T-0011 | US-customer-0014; US-maker-0011 | — |
| T-0080 | Customer order list query: GetCustomerOrders (paged + filtered) | 4 | M | draft | T-0060 | US-customer-0016 | — |
| T-0081 | Maker order list query: GetMakerOrders (paged + filtered) | 4 | M | draft | T-0060 | US-maker-0005 | — |
| T-0082 | Order detail query: GetOrderDetails (customer + maker variants with appropriate scoping) | 4 | M | draft | T-0060 | US-customer-0012; US-maker-0010 | — |
| T-0083 | Pending payment auto-cancel Function (timer hourly): orders in `PendingPayment` > 24h transitioned to `Cancelled` | 4 | S | draft | T-0063, T-0011 | US-customer-0010 | 0020 |
| T-0084 | Frontend: /objednavka (order placement form — Zásilkovna widget integration; personal pickup option; attachment upload; client-side validation mirrors) | 4 | L | draft | T-0063, T-0064, T-0065, T-0070 | US-customer-0010, 0011 | — |
| T-0085 | Frontend: /objednavka/potvrzeni (post-payment confirmation, handles ?status= query param) | 4 | S | draft | T-0067 | US-customer-0010 | — |
| T-0086 | Frontend: /dashboard/zakaznik (order list + filters) and /objednavka/[id] (order tracking, timeline, confirm-delivery button, message thread, attachments) | 4 | L | draft | T-0080, T-0082, T-0079, T-0076 | US-customer-0012, 0013, 0014, 0016 | — |
| T-0087 | Frontend: /dashboard/maker/objednavky and /dashboard/maker/objednavka/[id] (action buttons per state: Accept / Ship / Handed over; message thread; attachments) | 4 | L | draft | T-0081, T-0082, T-0071, T-0072, T-0073, T-0079 | US-maker-0005, 0006, 0007, 0008, 0010, 0011 | — |

**Phase 4 total:** 28 tickets. The bulkiest phase.

---

## Phase 5 — Post-order (reviews, payouts, admin operations)

| Ticket | Title | Phase | Size | State | Depends on | Stories | ADRs |
|---|---|---|---|---|---|---|---|
| T-0100 | Review entity + IReviewRepository; SubmitReview command (atomically updates Maker.rating_avg/count); RespondToReview command | 5 | M | draft | T-0060, T-0033 | US-customer-0015; US-maker-0014 | — |
| T-0101 | PayoutBatch entity + IPayoutBatchNumberGenerator (VYP-CZ format); IPayoutBatchRepository | 5 | M | draft | T-0007, T-0010 | US-admin-0007 | 0009 |
| T-0102 | CreatePayoutBatch command (admin + weekly cron): claims Delivered orders, generates fee invoices per maker, generates bank CSV, transitions orders to PayoutBatchId | 5 | L | draft | T-0101, T-0068, T-0042 | US-admin-0007 | 0009, 0014 |
| T-0103 | MarkPayoutBatchCompleted command (admin marks batch as paid by bank); orders Delivered → Completed; outbox `payout-sent` emails to makers | 5 | M | draft | T-0102, T-0011 | US-admin-0007; US-maker-0012 | 0014 |
| T-0104 | RunWeeklyPayoutBatch Function (timer Monday 02:00 UTC + HTTP-triggered) | 5 | S | draft | T-0102 | US-admin-0007 | 0020 |
| T-0105 | RefundOrder command (admin) + Comgate refund call; outbox order-refunded email | 5 | M | draft | T-0067, T-0066 | US-admin-0008 | 0014, 0016 |
| T-0106 | OpenDispute + ResolveDispute commands; auto-deliver skips disputed orders | 5 | M | draft | T-0060, T-0011, T-0077 | US-admin-0011 | 0014 |
| T-0107 | ChangeOrderStateManually command (admin escape hatch; restricted transitions; required reason) | 5 | M | draft | T-0060 | US-admin-0010 | 0014 |
| T-0108 | UpdateCountryConfiguration command (admin) with confirm-on-provider-change | 5 | M | draft | T-0010 | US-admin-0006 | 0014 |
| T-0109 | RetryOutboxEvent + AcknowledgeOutboxEvent commands (admin) | 5 | S | draft | T-0011 | US-admin-0014 | 0014, 0020 |
| T-0110 | DeleteUserPermanently command (GDPR; anonymizes related entities; only place hard-delete runs) | 5 | M | draft | T-0033, T-0060 | US-admin-0016 | 0013, 0014 |
| T-0111 | Admin queries: GetAllOrders (Unscoped), GetAllInvoices (Unscoped), GetAdminAuditLog (paged + filtered) | 5 | M | draft | T-0060, T-0068, T-0011 | US-admin-0009, 0012, 0015 | 0013 |
| T-0112 | Maker queries: GetMakerPayouts (paged), GetMakerOutboxEventsForOrder | 5 | M | draft | T-0101, T-0011 | US-maker-0012, 0013, 0017 | — |
| T-0113 | EvictExpiredRegistryCache Function (timer daily 02:00 UTC) | 5 | S | draft | T-0032 | — | 0020 |
| T-0114 | DataRetentionCleanup Function (timer weekly Sunday 03:00 UTC) — placeholder; full GDPR retention policy in v1.1 | 5 | S | draft | T-0011 | — | 0023 |
| T-0115 | Frontend: /dashboard/zakaznik review submission UI | 5 | S | draft | T-0100 | US-customer-0015 | — |
| T-0116 | Frontend: /dashboard/maker/vyplaty (payout list, batch detail with per-order breakdown, fee-invoice download) | 5 | M | draft | T-0103, T-0112 | US-maker-0012, 0013 | — |
| T-0117 | Frontend: /dashboard/maker review-reply UI | 5 | S | draft | T-0100 | US-maker-0014 | — |
| T-0118 | Frontend: /dashboard/admin overview + all-orders + all-invoices + audit-log views; manual state change + refund + dispute resolution UI; outbox retry UI; country config edit UI | 5 | L | draft | T-0102, T-0105, T-0106, T-0107, T-0108, T-0109, T-0111 | US-admin-0002, 0006, 0007, 0008, 0009, 0010, 0011, 0012, 0014, 0015 | — |
| T-0119 | Frontend: /dashboard/admin/categories CRUD + /dashboard/admin/makery list (verify, deactivate, refresh-ARES) | 5 | M | draft | T-0040, T-0034 | US-admin-0003, 0004, 0005, 0013 | — |

**Phase 5 total:** 20 tickets.

---

## Phase 6 — Polish (static pages, content, SEO, manual ops checks)

| Ticket | Title | Phase | Size | State | Depends on | Stories | ADRs |
|---|---|---|---|---|---|---|---|
| T-0130 | Static pages content: /jak-to-funguje, /pro-makery, /vop, /gdpr | 6 | M | draft | T-0015 | — | — |
| T-0131 | SEO: sitemap.xml, robots.txt, OG meta on landing + catalog + product pages | 6 | M | draft | T-0046, T-0047, T-0048 | — | — |
| T-0132 | k6 load test script + run; assess against perf budgets from ADR 0023 | 6 | M | draft | All Phase 1-5 | — | 0023 |
| T-0133 | Accessibility audit pass: axe-core in CI; manual keyboard nav; NVDA + Firefox screen-reader check on critical paths | 6 | M | draft | All frontend | — | 0023 |
| T-0134 | Production secret rotation playbook + monitoring playbook + restore-from-backup playbook (`docs/runbooks/`) | 6 | M | draft | T-0016 | — | 0023 |
| T-0135 | Bug bash + final manual smoke against staging | 6 | M | draft | All other tickets | — | — |

**Phase 6 total:** 6 tickets.

---

## Totals

| Phase | Tickets | Effort (approx) |
|---|---|---|
| 1 — Foundation scaffold | 16 | ~10 days |
| 2 — Identity | 17 | ~12 days |
| 3 — Catalog | 10 | ~6 days |
| 4 — Orders | 28 | ~20 days |
| 5 — Post-order | 20 | ~12 days |
| 6 — Polish | 6 | ~5 days |
| **Total** | **97** | **~65 days** of agent-equivalent work |

This is a rough rolling estimate. Some tickets in Phase 4 will reveal sub-tickets during implementation (e.g. label-streaming edge cases). PM splits as needed.

---

## Sprint plan (proposed)

The agents work continuously, but it's helpful to define checkpoints. Sprint = ~1 work week, ~10–15 tickets done.

| Sprint | Phases covered | Goal at end |
|---|---|---|
| 1 | Phase 1 (1–16) | Solution scaffolded, hosts run, OpenAPI emitted, NSwag pipeline works, Bicep deploys an empty environment |
| 2 | Phase 2 first half (20–32) | Auth (password + magic link + Google OAuth + reset + confirmation) end-to-end; addresses; geocoding; ARES lookup |
| 3 | Phase 2 second half (33–36) + Phase 3 first half (40–42) | Maker registration end-to-end; product CRUD with images |
| 4 | Phase 3 second half (43–49) | Catalog, maker profile, product detail pages live |
| 5 | Phase 4 first third (60–69) | Order placement → payment → invoice generation works end-to-end (no UI for tracking yet) |
| 6 | Phase 4 second third (70–79) | Shipping (Zásilkovna + personal pickup), accept/ship/deliver, messages |
| 7 | Phase 4 last third + Phase 5 first quarter (80–87, 100, 101) | Order UI complete; reviews entity in place; payout entity scaffolded |
| 8 | Phase 5 second half (102–112) | Weekly payouts, refunds, disputes, admin operations |
| 9 | Phase 5 last + Phase 6 (113–135) | Frontend admin UI; polish; load test; accessibility; runbooks |
| 10 | Production launch sprint | Deploy to production, soft launch, watch metrics |

Total: ~10 working weeks from sign-off to production launch. Conservative.

---

## Status

All tickets are in `draft`. PM expands each to a full ticket file on transition to `ready`. Builds begin in Phase 0.6 (scaffold) after user sign-off (Batch 10).
