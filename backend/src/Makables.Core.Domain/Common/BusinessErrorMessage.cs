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

    // === Product ===
    public const string ProductNotFound = "product.notFound";
    public const string ProductNotActive = "product.notActive";

    // === Maker ===
    public const string MakerNotFound = "maker.notFound";
    public const string MakerNotActive = "maker.notActive";
    public const string MakerAlreadyVerified = "maker.alreadyVerified";
    public const string MakerIcoAlreadyRegistered = "maker.icoAlreadyRegistered";

    // === Company registry (T-0032) ===
    public const string CompanyNotFound = "company.notFound";
    public const string CompanyRegistryTransient = "company.registryTransient";
    public const string CompanyRegistryPermanent = "company.registryPermanent";

    // === Country / config ===
    public const string CountryNotServiced = "country.notServiced";
    public const string CountryConfigMissing = "country.configMissing";
    public const string CountryProviderNotRegistered = "country.providerNotRegistered";

    // === Payment ===
    public const string PaymentGatewayUnavailable = "payment.gatewayUnavailable";
    public const string PaymentVerificationFailed = "payment.verificationFailed";
    public const string PaymentGatewayMisconfigured = "payment.gatewayMisconfigured";

    // === Shipping ===
    public const string ShippingCarrierUnavailable = "shipping.carrierUnavailable";

    // === Review ===
    public const string ReviewAlreadyExists = "review.alreadyExists";
    public const string ReviewOrderNotDelivered = "review.orderNotDelivered";

    // === File ===
    public const string FileInvalid = "file.invalid";
    public const string FileTooLarge = "file.tooLarge";

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
