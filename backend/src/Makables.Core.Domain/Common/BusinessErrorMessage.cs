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

    // === Company registry (T-0032) ===
    public const string CompanyNotFound = "company.notFound";
    public const string CompanyRegistryTransient = "company.registryTransient";
    public const string CompanyRegistryPermanent = "company.registryPermanent";

    // === Country / config ===
    public const string CountryNotServiced = "country.notServiced";
    public const string CountryConfigMissing = "country.configMissing";
    public const string CountryProviderNotRegistered = "country.providerNotRegistered";
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
    public const string ShippingCarrierUnavailable = "shipping.carrierUnavailable";

    // === Review ===
    public const string ReviewAlreadyExists = "review.alreadyExists";
    public const string ReviewOrderNotDelivered = "review.orderNotDelivered";

    // === File ===
    public const string FileInvalid = "file.invalid";
    public const string FileTooLarge = "file.tooLarge";
    public const string FileUnsupportedType = "file.unsupportedType";

    // === Payout batch ===
    public const string PayoutBatchEmpty = "payoutBatch.empty";

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

    // === Outbox processor (T-0029) ===
    public const string OutboxQueuePublishFailed = "outbox.queuePublishFailed";
    public const string OutboxRowNotFound = "outbox.rowNotFound";

    // === Geocoder (T-0031) ===
    public const string GeocoderInvalidInput = "geocoder.invalidInput";
    public const string GeocoderNoMatch = "geocoder.noMatch";
    public const string GeocoderTransientFailure = "geocoder.transientFailure";
    public const string GeocoderPermanentFailure = "geocoder.permanentFailure";
}
