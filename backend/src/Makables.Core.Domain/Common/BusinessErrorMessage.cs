namespace Makables.Core.Domain.Common;

/// <summary>
/// Canonical, dot-notation error codes used across the platform. Every code
/// here has a matching i18n key in <c>frontend/src/lib/i18n/cs-CZ/</c>
/// (L10n agent enforces parity). Per ADR 0002 and patterns §A.4.
/// </summary>
public static class BusinessErrorMessage
{
    // === Auth ===
    public const string AuthRequired = "auth.required";
    public const string AuthForbidden = "auth.forbidden";
    public const string AuthEmailAlreadyExists = "auth.emailAlreadyExists";
    public const string AuthEmailNotConfirmed = "auth.emailNotConfirmed";
    public const string AuthInvalidCredentials = "auth.invalidCredentials";
    public const string AuthLocked = "auth.locked";
    public const string AuthRateLimited = "auth.rateLimited";
    public const string AuthPasswordTooCommon = "auth.passwordTooCommon";
    public const string AuthCurrentPasswordWrong = "auth.currentPasswordWrong";
    public const string AuthOAuthNotAllowedForAdmin = "auth.oauthNotAllowedForAdmin";
    public const string AuthMagicLinkInvalid = "auth.magicLinkInvalid";
    public const string AuthEmailConfirmationInvalid = "auth.emailConfirmationInvalid";
    public const string AuthPasswordResetInvalid = "auth.passwordResetInvalid";
    public const string AuthOAuthInvalidState = "auth.oauthInvalidState";
    public const string AuthOAuthEmailNotVerified = "auth.oauthEmailNotVerified";
    public const string AuthOAuthExchangeFailed = "auth.oauthExchangeFailed";

    // === Validation ===
    public const string Required = "validation.required";
    public const string MinLength = "validation.minLength";
    public const string MaxLength = "validation.maxLength";
    public const string MinValue = "validation.minValue";
    public const string InvalidEnumValue = "validation.invalidEnumValue";
    public const string InvalidEmailFormat = "validation.invalidEmail";
    public const string InvalidPhoneFormat = "validation.invalidPhone";
    public const string InvalidZipFormat = "validation.invalidZip";
    public const string IcoFormatInvalid = "validation.icoFormat";
    public const string InvalidBankAccountFormat = "validation.bankAccountFormat";
    public const string ValidationFailed = "validation.failed";

    // === Order ===
    public const string OrderNotFound = "order.notFound";
    public const string OrderAlreadyAccepted = "order.alreadyAccepted";
    public const string OrderInvalidTransition = "order.invalidTransition";
    public const string OrderNotPayableYet = "order.notPayableYet";
    /// <summary>
    /// Quantity must equal 1 at MVP. T-0061 user decision Q4 keeps the
    /// pricing snapshot scalar and the Order entity single-line; T-0063
    /// CreateOrder.Validator fails loud (rather than silently truncating)
    /// when the customer-supplied quantity is anything but 1.
    /// </summary>
    public const string OrderInvalidQuantity = "order.invalidQuantity";
    /// <summary>
    /// Per-order cap on customer attachments hit (10 at MVP, see
    /// <see cref="Orders.Order.MaxAttachmentCount"/>). T-0064 user
    /// decision baked into the AddAttachment / upload contract.
    /// </summary>
    public const string OrderAttachmentLimitReached = "order.attachmentLimitReached";
    /// <summary>
    /// Upload attempted on an order whose state no longer admits new
    /// attachments — see <see cref="Orders.Order.AllowsAttachmentUpload"/>
    /// for the open window (PendingPayment / Paid / Accepted only).
    /// T-0064 user decision Q4.
    /// </summary>
    public const string OrderStateForbidsAttachment = "order.stateForbidsAttachment";
    /// <summary>
    /// Download requested for an attachment that does not exist OR is
    /// owned by another customer / assigned to another maker. Same
    /// IDOR-leak-resistant 404 shape as <see cref="OrderNotFound"/>.
    /// T-0064.
    /// </summary>
    public const string OrderAttachmentNotFound = "order.attachmentNotFound";
    /// <summary>
    /// <c>CreatePaymentSession</c> called on an order whose state is no
    /// longer eligible for payment (anything other than
    /// <see cref="Orders.OrderState.PendingPayment"/>). Distinct from
    /// <see cref="OrderPaymentAlreadyCaptured"/> so the frontend can
    /// distinguish "already paid, navigate to receipt" from "cancelled /
    /// shipped / delivered, you can't pay this anymore". T-0065.
    /// </summary>
    public const string OrderInvalidStateForPayment = "order.invalidStateForPayment";
    /// <summary>
    /// The verify-then-recreate retry path discovered the order's existing
    /// Comgate session is already <see cref="Payments.PaymentState.Paid"/>
    /// (or <see cref="Payments.PaymentState.Refunded"/>) — meaning the
    /// webhook should already have transitioned the row past
    /// <see cref="Orders.OrderState.PendingPayment"/>. Surfaces as a
    /// state-machine mismatch + Critical log so ops can reconcile before
    /// the customer double-pays. T-0065 user decision Q1.
    /// </summary>
    public const string OrderPaymentAlreadyCaptured = "order.paymentAlreadyCaptured";

    // === Order messages (T-0079) ===
    /// <summary>
    /// Validator rejected an empty / whitespace-only message body on
    /// PostCustomer/MakerOrderMessage. T-0079 §C.13.
    /// </summary>
    public const string OrderMessageBodyEmpty = "order.message.bodyEmpty";

    /// <summary>
    /// Validator rejected a message body exceeding 2000 characters on
    /// PostCustomer/MakerOrderMessage. T-0079 §C.13.
    /// </summary>
    public const string OrderMessageBodyTooLong = "order.message.bodyTooLong";

    /// <summary>
    /// PostCustomer/MakerOrderMessage refused because the order is still
    /// in <see cref="Orders.OrderState.PendingPayment"/>. Per the T-0079
    /// review fold (user ruling on MEDIUM-2): the thread opens once the
    /// order is paid; ALL later states — including Cancelled and Disputed
    /// — stay open for post-cancel coordination.
    /// </summary>
    public const string OrderMessageNotAllowedInState = "order.message.notAllowedInState";

    // === Manual state change (T-0107, user-locked Q4 strict allow-list) ===
    /// <summary>
    /// The requested manual transition is outside the allow-list and has
    /// no sanctioned command (e.g. resurrecting a Cancelled order, any
    /// move out of Refunded, skipping lifecycle steps). T-0107.
    /// </summary>
    public const string OrderManualTransitionNotAllowed = "order.manualTransition.notAllowed";
    /// <summary>
    /// Blocked: → Refunded (money must move at the provider first) and
    /// Paid/Accepted → Cancelled (captured money would be stranded).
    /// Sanctioned command: <c>RefundOrder</c> (T-0105). T-0107.
    /// </summary>
    public const string OrderManualTransitionUseRefundOrder = "order.manualTransition.useRefundOrder";
    /// <summary>
    /// Blocked: → Disputed (a Disputed order needs its Dispute row).
    /// Sanctioned command: <c>OpenDispute</c> (T-0106). T-0107.
    /// </summary>
    public const string OrderManualTransitionUseOpenDispute = "order.manualTransition.useOpenDispute";
    /// <summary>
    /// Blocked: any manual move out of Disputed (the restore + outcome
    /// edges belong to the resolution flow). Sanctioned command:
    /// <c>ResolveDispute</c> (T-0106). T-0107.
    /// </summary>
    public const string OrderManualTransitionUseResolveDispute = "order.manualTransition.useResolveDispute";
    /// <summary>
    /// Blocked: Delivered → Completed (Completed means "maker payout
    /// settled"; a manual flip would fake a payout). Sanctioned command:
    /// <c>MarkPayoutBatchCompleted</c> (T-0103). T-0107.
    /// </summary>
    public const string OrderManualTransitionUseMarkPayoutBatchCompleted = "order.manualTransition.useMarkPayoutBatchCompleted";
    /// <summary>
    /// Blocked: → Paid on an order with no <c>PaymentProviderRef</c> —
    /// there is no payment to point at; marking Paid would fabricate
    /// revenue with no provider trail and permanently break the T-0105
    /// refund path for that order. T-0107.
    /// </summary>
    public const string OrderManualTransitionPaidRequiresProviderRef = "order.manualTransition.paidRequiresProviderRef";

    // === Order disputes (T-0106) ===
    /// <summary>
    /// Customer/maker submitted a carrier-reserved dispute category
    /// (CarrierReturned / CarrierFailed) — those are set only by the
    /// T-0078 carrier sweep or by an admin transcribing a phone report.
    /// T-0106 §C.6.
    /// </summary>
    public const string OrderDisputeCategoryNotAllowed = "order.dispute.categoryNotAllowed";
    /// <summary>
    /// Resolve attempted on an order with no OPEN dispute (state is not
    /// Disputed, the dispute row is already resolved, or a double-resolve
    /// race lost). Loud 409 — a Silent-Success second resolve with a
    /// different outcome would mask an admin race. T-0106 §C.4.
    /// </summary>
    public const string OrderDisputeNotOpen = "order.dispute.notOpen";

    // === Maker (T-0063 defence-in-depth on maker state) ===
    /// <summary>
    /// The maker's row exists but <c>Auditable.IsActive</c> is false (or
    /// the row is missing entirely after a soft-delete cascade). T-0063
    /// CreateOrder.Handler refuses to place an order against a
    /// deactivated maker even if the frontend gate slipped.
    /// </summary>
    public const string MakerDeactivated = "maker.deactivated";
    /// <summary>
    /// The maker has not yet been admin-verified (<c>Maker.IsVerified ==
    /// false</c>). T-0063 CreateOrder.Handler refuses the order — every
    /// money-bearing flow requires a verified maker per US-customer-0010
    /// AC-1's pre-condition.
    /// </summary>
    public const string MakerNotVerified = "maker.notVerified";
    /// <summary>
    /// The customer chose <see cref="Orders.ShippingMethod.PersonalPickup"/>
    /// but the maker has <c>PersonalPickupEnabled == false</c>. T-0063
    /// CreateOrder.Handler fails fast so the maker isn't stuck with an
    /// order they can't fulfil.
    /// </summary>
    public const string MakerPersonalPickupDisabled = "maker.personalPickupDisabled";

    // === Product ===
    public const string ProductNotFound = "product.notFound";
    public const string ProductNotActive = "product.notActive";
    public const string ProductImageLimitReached = "product.imageLimitReached";
    public const string ProductImageNotFound = "product.imageNotFound";
    public const string ProductPriceNegative = "product.priceNegative";
    public const string ProductFreeRequiresOnRequest = "product.freeRequiresOnRequest";
    public const string ProductCurrencyMismatch = "product.currencyMismatch";
    /// <summary>
    /// Product cannot be ordered directly through the standard checkout
    /// path — e.g. <see cref="Products.PriceType.OnRequest"/> requires
    /// the (post-MVP) custom-quote flow. T-0061
    /// <c>PricingService.ComputeForProductAsync</c>.
    /// </summary>
    public const string ProductNotOrderable = "product.notOrderable";

    // === Category (T-0040) ===
    public const string CategoryNotFound = "category.notFound";
    public const string CategoryNotActive = "category.notActive";
    public const string CategorySlugAlreadyExists = "category.slugAlreadyExists";

    // === Blob storage (T-0042) ===
    public const string BlobNotFound = "blob.notFound";
    public const string BlobUploadFailed = "blob.uploadFailed";
    public const string BlobDownloadFailed = "blob.downloadFailed";
    // Generic operation failure for delete / exists probes so a caller
    // (and logs/metrics keyed off the code) can tell them apart from a
    // download failure. T-0042 Copilot review.
    public const string BlobOperationFailed = "blob.operationFailed";
    public const string BlobInvalidContainer = "blob.invalidContainer";
    public const string BlobInvalidPath = "blob.invalidPath";

    // === Maker ===
    public const string MakerNotFound = "maker.notFound";
    public const string MakerNotActive = "maker.notActive";
    public const string MakerAlreadyVerified = "maker.alreadyVerified";
    public const string MakerIcoAlreadyRegistered = "maker.icoAlreadyRegistered";
    /// <summary>
    /// ARES reports the company as no longer trading (dissolved /
    /// terminated). T-0033 <c>RegisterMaker</c> refuses to register a
    /// dissolved entity as a platform maker. Distinct from
    /// <see cref="MakerIcoAlreadyRegistered"/>: that means the IČO is
    /// already on Makables; this means the IČO is not active in the
    /// state registry at all.
    /// </summary>
    public const string MakerCompanyDissolved = "maker.companyDissolved";
    public const string MakerSlugAlreadyExists = "maker.slugAlreadyExists";

    /// <summary>
    /// FK invariant violation: a <see cref="Makers.Maker"/> aggregate exists
    /// for the given id but its linked <c>UserId</c> resolves to no row in
    /// <c>users</c>. Distinct from <see cref="MakerNotFound"/> so the T-0029
    /// triage UI (and ops alerting) can separate "maker soft-deleted" from
    /// "data corruption — maker exists without its user". T-0067 reviewer M-2.
    /// </summary>
    public const string MakerUserMissing = "maker.userMissing";

    /// <summary>
    /// FK invariant violation: an <see cref="Orders.Order"/> exists but its
    /// <c>CustomerUserId</c> resolves to no row in <c>users</c>. Used by
    /// <see cref="Features.Orders.MarkOrderPaid"/> to refuse to commit when
    /// the customer-user row is unexpectedly null — the Comgate webhook
    /// retries, ops investigates. T-0067 reviewer M-3 (symmetric with the
    /// maker-user-missing failure path).
    /// </summary>
    public const string OrderCustomerUserMissing = "order.customerUserMissing";

    // === Company registry (T-0032) ===
    public const string CompanyNotFound = "company.notFound";
    public const string CompanyRegistryTransient = "company.registryTransient";
    public const string CompanyRegistryPermanent = "company.registryPermanent";

    // === Country / config ===
    public const string CountryNotServiced = "country.notServiced";
    public const string CountryConfigMissing = "country.configMissing";
    public const string CountryProviderNotRegistered = "country.providerNotRegistered";
    /// <summary>
    /// The retyped <c>ConfirmedProviderCode</c> does not match the new
    /// provider value (T-0108 / US-admin-0006 AC-3 high-stakes retype
    /// interlock failed). Surfaced as a 400.
    /// </summary>
    public const string CountryProviderConfirmationMismatch = "country.providerConfirmationMismatch";
    /// <summary>
    /// The <c>CountryConfiguration</c> row for the requested country code
    /// is missing — used by <see cref="ProductNotOrderable"/>-adjacent
    /// flows where the upstream country lookup fails and the caller wants
    /// a typed NotFound rather than the generic
    /// <see cref="CountryConfigMissing"/>. T-0061.
    /// </summary>
    public const string CountryConfigurationNotFound = "countryConfiguration.notFound";

    // === Payment ===
    public const string PaymentGatewayUnavailable = "payment.gatewayUnavailable";
    public const string PaymentVerificationFailed = "payment.verificationFailed";
    public const string PaymentGatewayMisconfigured = "payment.gatewayMisconfigured";
    /// <summary>
    /// HTTP-level failure talking to the payment provider — network blip,
    /// timeout, 5xx, 408, 429. Classified <see cref="ErrorType.Transient"/>;
    /// the customer can retry. T-0065.
    /// </summary>
    public const string PaymentProviderUnavailable = "payment.providerUnavailable";
    /// <summary>
    /// Provider returned a business error (e.g. invalid currency,
    /// insufficient merchant balance). Classified
    /// <see cref="ErrorType.Permanent"/> — retrying won't help. T-0065.
    /// </summary>
    public const string PaymentProviderRejected = "payment.providerRejected";
    /// <summary>
    /// Provider says the merchant id or shared secret is wrong. Classified
    /// <see cref="ErrorType.Configuration"/> and logged at
    /// <see cref="Microsoft.Extensions.Logging.LogLevel.Critical"/> —
    /// ops must intervene; no retry. T-0065.
    /// </summary>
    public const string PaymentProviderMisconfigured = "payment.providerMisconfigured";
    /// <summary>
    /// <see cref="Payments.IPaymentProviderFactory.ResolveAsync"/> could not
    /// find a keyed <see cref="Payments.IPaymentProvider"/> for the country's
    /// configured provider code. T-0065.
    /// </summary>
    public const string PaymentProviderNotRegistered = "payment.providerNotRegistered";
    /// <summary>
    /// Provider returned an unrecognised error shape. Classified
    /// <see cref="ErrorType.Unknown"/> — limited retry then escalate
    /// (Mapbox / ARES precedent: 3 attempts). T-0065.
    /// </summary>
    public const string PaymentUnknownError = "payment.unknownError";

    // === Payment refund (T-0105) ===
    /// <summary>
    /// Refund attempted on an order whose state is outside the refundable
    /// window (Paid / Accepted / Shipped / Delivered / Completed).
    /// PendingPayment has no captured money; Cancelled / Refunded are
    /// terminal; Disputed routes through T-0106's ResolveDispute (restore
    /// PreDisputeState, then refund — user decision Q2). T-0105.
    /// </summary>
    public const string PaymentRefundInvalidState = "payment.refund.invalidState";
    /// <summary>
    /// Admin-entered amount exceeds <c>Order.RemainingRefundableMinor</c>
    /// (total − cumulative refunded). The provider is never called on
    /// this failure — the pre-flight predicate blocks first. T-0105 Q1.
    /// </summary>
    public const string PaymentRefundAmountExceedsRemaining = "payment.refund.amountExceedsRemaining";
    /// <summary>
    /// Refund on a Completed order (maker payout already settled)
    /// requires the explicit <c>AcknowledgePostPayout</c> flag — the
    /// platform fronts the refund; maker-share recovery is manual at MVP
    /// (negative-balance ledger lands with T-0102). T-0105 Q5.
    /// </summary>
    public const string PaymentRefundPostPayoutAckRequired = "payment.refund.postPayoutAckRequired";
    /// <summary>
    /// The order has no <c>PaymentProviderRef</c> — there is no captured
    /// payment to point the Comgate /v1.0/refund call at. Surfaced
    /// before the provider is touched. T-0105.
    /// </summary>
    public const string PaymentRefundNoProviderRef = "payment.refund.noProviderRef";

    // === Payment webhook (T-0066) ===
    /// <summary>
    /// The inbound provider webhook body is missing required fields
    /// (<c>transId</c> / <c>refId</c>) or is not form-urlencoded. The
    /// controller returns 400 so the provider retries; ops-side alert
    /// surfaces if the rate spikes. T-0066.
    /// </summary>
    public const string PaymentWebhookMalformed = "payment.webhook.malformed";
    /// <summary>
    /// The webhook arrived from an IP that is not in
    /// <c>ComgateOptions.WebhookAllowedIps</c>. Logged inside the
    /// <c>ComgateWebhookIpAllowlist</c> filter; the response is a
    /// bare 401 with no body (we never parsed the body), so this code
    /// surfaces in our logs only, never to the caller. T-0066.
    /// </summary>
    public const string PaymentWebhookIpRejected = "payment.webhook.ipRejected";
    /// <summary>
    /// The body's <c>refId</c> does not match the order found by
    /// <c>transId</c> — possible spoof attempt. Controller returns 401
    /// (forces provider retry, ops alert fires); the handler's
    /// defence-in-depth check ALSO returns this code as a
    /// <see cref="ErrorType.Conflict"/> if reached. T-0066.
    /// </summary>
    public const string PaymentWebhookRefIdMismatch = "payment.webhook.refIdMismatch";

    // === Shipping ===
    /// <summary>
    /// HTTP-level transient failure talking to the carrier — network blip,
    /// timeout, 5xx, 408, 429. Classified <see cref="ErrorType.Transient"/>.
    /// T-0070.
    /// </summary>
    public const string ShippingCarrierUnavailable = "shipping.carrierUnavailable";

    /// <summary>
    /// Carrier rejected the shipment because the order's weight is out of
    /// the supported range. Classified <see cref="ErrorType.Permanent"/> —
    /// retrying won't help. T-0070.
    /// </summary>
    public const string ShippingCarrierInvalidWeight = "shipping.invalidWeight";

    /// <summary>
    /// Carrier rejected the shipment because the supplied pickup-point
    /// (Packeta <c>addressId</c>) is no longer valid (deprecated branch).
    /// Classified <see cref="ErrorType.Permanent"/>; customer picks a new
    /// branch. T-0070.
    /// </summary>
    public const string ShippingCarrierAddressIdNotFound = "shipping.addressIdNotFound";

    /// <summary>
    /// Carrier rejected the API call with an auth / configuration error
    /// (wrong API key, wrong sender label). Classified
    /// <see cref="ErrorType.Configuration"/> — ops must intervene; logged
    /// at Critical so an alert fires. T-0070.
    /// </summary>
    public const string ShippingCarrierConfigurationError = "shipping.configurationError";

    /// <summary>
    /// Maker called the wrong shipping endpoint for this order's
    /// <c>ShippingMethod</c> (e.g. POSTed /handover for a Zásilkovna order
    /// or /ship for a PersonalPickup order). Classified
    /// <see cref="ErrorType.Validation"/>. T-0072.
    /// </summary>
    public const string ShippingMethodNotEligible = "shipping.methodNotEligible";

    // === Review (T-0100) ===
    /// <summary>An active review already exists for the order (US-customer-0015 AC-2).</summary>
    public const string ReviewAlreadyExists = "review.alreadyExists";
    /// <summary>Review submitted on an order not yet Delivered/Completed (US-customer-0015 AC-3).</summary>
    public const string ReviewOrderNotDelivered = "review.orderNotDelivered";
    /// <summary><c>SubmitReview</c> Validator: rating outside 1..5 (US-customer-0015 AC-6).</summary>
    public const string ReviewRatingOutOfRange = "review.ratingOutOfRange";
    /// <summary><c>SubmitReview</c> Validator: body exceeds 1000 chars (US-customer-0015 AC-6).</summary>
    public const string ReviewBodyTooLong = "review.bodyTooLong";
    /// <summary><c>RespondToReview</c> Validator: empty/whitespace reply.</summary>
    public const string ReviewReplyEmpty = "review.replyEmpty";
    /// <summary><c>RespondToReview</c> Validator: reply exceeds 500 chars (US-maker-0014 AC-10).</summary>
    public const string ReviewReplyTooLong = "review.replyTooLong";
    /// <summary>
    /// Maker-side cross-tenant / missing-review 404 path
    /// (US-maker-0014 AC-2/AC-9). <c>OrderNotFound</c> is REUSED for the
    /// customer-side cross-tenant order miss (no new code).
    /// </summary>
    public const string ReviewNotFound = "review.notFound";

    // === File ===
    public const string FileInvalid = "file.invalid";
    public const string FileTooLarge = "file.tooLarge";
    public const string FileUnsupportedType = "file.unsupportedType";

    // === Payout batch ===
    public const string PayoutBatchEmpty = "payoutBatch.empty";

    /// <summary>
    /// <see cref="Payouts.PayoutBatch.AttachCsvBlobPath"/> was called with a
    /// blob-path value that differs from the one already stored. Set-once:
    /// an idempotent retry with the SAME value succeeds; a real overwrite
    /// fails with this code. Mirrors
    /// <see cref="InvoiceBlobPathAlreadySet"/> (T-0068a). Admin / log only —
    /// the CSV formatter is deterministic so a real overwrite indicates a
    /// programmer error, not a user-facing action. T-0101.
    /// </summary>
    public const string PayoutBatchCsvPathAlreadySet = "payoutBatch.csvPathAlreadySet";

    /// <summary>
    /// A payout batch for this ISO week was already created and completed —
    /// pre-checked via <c>GetByNumberAsync</c> so a same-week re-run returns
    /// a typed, translated Conflict instead of dying on the unique index as
    /// a raw 500. Excluded orders (Q3/Q5) ride NEXT week's batch. T-0102a.
    /// </summary>
    public const string PayoutBatchWeekAlreadyProcessed = "payoutBatch.weekAlreadyProcessed";

    /// <summary>
    /// A concurrent <c>CreatePayoutBatch</c> lost the open-batch race: the
    /// handler's <c>GetOpenBatchAsync</c> null-check passed, but a parallel
    /// run committed an open <see cref="Payouts.PayoutBatchState.Processing"/>
    /// batch first, so the loser's <c>SaveChangesAsync</c> violated the
    /// partial-unique index <c>ux_payout_batches_open_per_country</c>. The
    /// translator surfaces this as a typed Conflict instead of a raw 500;
    /// the loser's entire UoW (batch + claims + fee invoices + outbox) rolls
    /// back atomically — no split/double claim. The next admin retry (or the
    /// T-0104 timer's next pass) hits the read-check and resumes the WINNER's
    /// batch as Silent Success. T-0102a (payout-core-bundle review BLOCKER-1).
    /// </summary>
    public const string PayoutBatchAlreadyOpen = "payoutBatch.alreadyOpen";

    /// <summary>
    /// Eligible orders in the run do not share a single currency. Defensive
    /// money math — summing mixed currencies into one
    /// <c>TotalAmountMinor</c> would silently corrupt the wire amount.
    /// Logged Critical. T-0102a §C.9.
    /// </summary>
    public const string PayoutBatchCurrencyMismatch = "payoutBatch.currencyMismatch";

    /// <summary>
    /// Admin CSV download requested for a batch id that does not exist.
    /// T-0102b §C.14.
    /// </summary>
    public const string PayoutBatchNotFound = "payoutBatch.notFound";

    /// <summary>
    /// Admin CSV download requested for a batch whose <c>CsvBlobPath</c> is
    /// still null (the artifact pipeline has not produced the CSV yet — a
    /// re-run completes it). T-0102b §C.14.
    /// </summary>
    public const string PayoutBatchCsvNotReady = "payoutBatch.csvNotReady";

    /// <summary>
    /// <see cref="Payouts.PayoutBatch.Complete"/> was targeted at a batch that
    /// is not <see cref="Payouts.PayoutBatchState.Processing"/> (and not
    /// already Completed — that path is the handler's Silent-Success echo).
    /// Structurally unreachable with the two-value enum today; the guard makes
    /// the domain method total and future-proofs a third state. T-0103.
    /// </summary>
    public const string PayoutBatchNotProcessing = "payoutBatch.notProcessing";

    // === Invoice (T-0068a) ===
    /// <summary>
    /// <see cref="Invoices.Invoice.AttachPdfBlobPath"/> was called with a
    /// blob-path value that differs from the one already stored on the
    /// invoice. The first attach is set-once; an idempotent retry with the
    /// SAME value succeeds, a real overwrite attempt fails with this code.
    /// Mirrors the <see cref="Orders.Order.MarkAsPaid"/> set-once pattern
    /// on <c>PaymentProviderRef</c> / <c>PaymentMethod</c> (T-0067).
    ///
    /// <para>
    /// Czech i18n key intentionally NOT shipped at T-0068a — this surface
    /// is admin / log only (T-0068b's IInvoiceService is the only caller;
    /// the renderer's blob upload is deterministic so a real overwrite
    /// indicates a programmer error or data corruption, not a user-facing
    /// action). l10n added in T-0068b alongside the StandardVat /
    /// non-VAT-payer template strings per locked decision absorption.
    /// </para>
    /// </summary>
    public const string InvoiceBlobPathAlreadySet = "invoice.blobPathAlreadySet";

    // === Invoice (T-0068b) ===
    /// <summary>
    /// <c>IssueInvoice.Handler</c> hit an <see cref="Configuration.InvoicingMode"/>
    /// that has no renderer template wired (<see cref="Configuration.InvoicingMode.ReverseCharge"/>
    /// or <see cref="Configuration.InvoicingMode.StrictFiscalReporting"/> per
    /// T-0068b locked decision 6 — both modes ship a Permanent failure
    /// until a follow-up ticket adds their templates). The outbox
    /// processor consumes the event; ops sees the code and triages.
    /// </summary>
    public const string InvoicingModeNotImplemented = "invoice.invoicingModeNotImplemented";

    /// <summary>
    /// QuestPdfInvoiceRenderer threw inside <c>RenderAsync</c> — typically
    /// a layout DSL bug or a font-loading failure. Classified
    /// <see cref="ErrorType.Permanent"/> because re-renders won't fix it;
    /// admin investigates the renderer code path. T-0068b.
    /// </summary>
    public const string InvoiceRenderFailed = "invoice.renderFailed";

    /// <summary>
    /// Blob upload of the rendered PDF failed.
    /// <see cref="ErrorType.Transient"/> for network-like failures
    /// (Azure throttled, connection blip) so the outbox replays via the
    /// stall mechanism; <see cref="ErrorType.Permanent"/> for shape-like
    /// failures (oversized path, invalid container) so admin sees the
    /// failed row immediately. T-0068b.
    /// </summary>
    public const string InvoiceBlobUploadFailed = "invoice.blobUploadFailed";

    // === Invoice email attachment (T-0069) ===
    /// <summary>
    /// <c>EmailSendService.SendOrderPaidCustomerEmailAsync</c> looked up the
    /// Invoice for the order and found either no row yet, or a row with
    /// <c>PdfBlobPath</c> still null. The render pipeline (GenerateInvoiceFunction
    /// → IssueInvoice.Command) has not finished yet — typical when the
    /// invoice.generate queue lost the FIFO race against the email queue.
    /// Classified <see cref="ErrorType.Transient"/>: the outbox retry policy
    /// re-delivers the email event after 1m / 5m / 15m ... and the second
    /// attempt almost always succeeds (render is sub-second on the happy
    /// path). T-0069 locked decision 1.
    /// </summary>
    public const string InvoiceNotYetRendered = "invoice.notYetRendered";

    /// <summary>
    /// Blob download of the PDF attachment failed inside
    /// <c>EmailSendService.SendOrderPaidCustomerEmailAsync</c> — either the
    /// blob is missing (data-integrity bug), auth is misconfigured (deploy
    /// bug), or Azure exhausted its internal retry budget (rare). Classified
    /// <see cref="ErrorType.Permanent"/> because all three are ops-investigation
    /// territory; retrying via outbox at 1m wastes budget and adds log noise.
    /// The outbox parks the row + a Critical log fires. T-0069 locked
    /// decision 1 (and AC-8 commentary).
    /// </summary>
    public const string InvoicePdfAttachmentDownloadFailed = "invoice.pdfAttachmentDownloadFailed";

    /// <summary>
    /// SendGrid rejected the message because the PDF attachment exceeds
    /// the provider's 30 MB cap. A normal invoice is &lt; 100 KB; surfacing
    /// this means a rendering bug or data-integrity issue (oversized PDF,
    /// embedded media). Classified <see cref="ErrorType.Permanent"/> per
    /// T-0069 locked decision 4: retrying never resolves a fixed size cap.
    /// The outbox stalls after the policy's attempt budget; the row sits
    /// in DB until ops investigates the renderer.
    /// </summary>
    public const string InvoicePdfAttachmentTooLarge = "invoice.pdfAttachmentTooLarge";

    // === User ===
    public const string UserCannotDeleteWithInFlightOrders = "user.cannotDeleteWithInFlightOrders";

    // === Email pipeline (T-0028) ===
    public const string EmailTemplateNotFound = "email.templateNotFound";
    public const string EmailTemplateTranslationMissing = "email.translationMissing";
    public const string EmailProviderTransientFailure = "email.providerTransient";
    public const string EmailProviderPermanentFailure = "email.providerPermanent";
    // Split per T-0028 CQ reviewer N-4: T-0029's outbox-row triage UI needs
    // to distinguish "decode crashed" (malformed JSON) from "decode succeeded
    // but fields are blank" (producer wrote a partial payload).
    public const string EmailPayloadMalformed = "email.payloadMalformed";
    public const string EmailPayloadMissingFields = "email.payloadMissingFields";
    public const string EmailEventTypeUnknown = "email.eventTypeUnknown";
    /// <summary>
    /// <c>EmailOptions.AdminNotificationAddress</c> (env
    /// <c>ADMIN_NOTIFICATION_EMAIL</c>) is not configured at send time of
    /// an <c>order.disputed.adminEmail</c> event. Classified
    /// <see cref="ErrorType.Configuration"/> per ADR 0020 — the outbox row
    /// parks visibly for ops and is retried after the config fix; the
    /// dispute open itself is never blocked by a missing notification
    /// recipient. T-0106 §C.9.
    /// </summary>
    public const string EmailAdminRecipientNotConfigured = "email.adminRecipientNotConfigured";
    /// <summary>
    /// The outbox event type is one of the order-email variants but the
    /// payload JSON could not be decoded into the matching record (e.g.
    /// the producer enqueued a partial / malformed shape). Distinct from
    /// <see cref="EmailPayloadMalformed"/> so T-0029's triage UI can show
    /// the two classes of bug separately ("auth-flow payload bug" vs
    /// "order-flow payload bug"). T-0067.
    /// </summary>
    public const string OrderEmailPayloadMalformed = "email.orderPayloadMalformed";

    // === Outbox processor (T-0029) ===
    public const string OutboxQueuePublishFailed = "outbox.queuePublishFailed";
    public const string OutboxRowNotFound = "outbox.rowNotFound";
    /// <summary>
    /// Admin force-retry (T-0109) targeted a row whose <c>ProcessedAt</c> is
    /// already set — there is nothing to retry on a drained row. Surfaced as
    /// a clean 409 rather than a Silent-Success so the operator sees they
    /// clicked Retry on a row that already ran (locked A.3).
    /// </summary>
    public const string OutboxAlreadyProcessed = "outbox.alreadyProcessed";

    // === Geocoder (T-0031) ===
    public const string GeocoderInvalidInput = "geocoder.invalidInput";
    public const string GeocoderNoMatch = "geocoder.noMatch";
    public const string GeocoderTransientFailure = "geocoder.transientFailure";
    public const string GeocoderPermanentFailure = "geocoder.permanentFailure";
}
