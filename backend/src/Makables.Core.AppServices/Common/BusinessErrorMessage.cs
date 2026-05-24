namespace Makables.Core.AppServices.Common;

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

    // === Validation ===
    public const string Required = "validation.required";
    public const string MinLength = "validation.minLength";
    public const string MaxLength = "validation.maxLength";
    public const string InvalidEnumValue = "validation.invalidEnumValue";
    public const string InvalidEmailFormat = "validation.invalidEmail";
    public const string InvalidPhoneFormat = "validation.invalidPhone";
    public const string InvalidZipFormat = "validation.invalidZip";
    public const string InvalidIcoFormat = "validation.icoFormat";
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

    // === Company registry ===
    public const string CompanyNotFound = "company.notFound";

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
}
