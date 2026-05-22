namespace Makables.Core.Domain.Common;

/// <summary>
/// An expected failure. Carries a field name (or empty), a dot-notation code
/// (matching i18n keys in the frontend), the <see cref="ErrorType"/>
/// classification, and optional details. Per ADR 0002 and patterns §A.4.
/// </summary>
public sealed record Error(
    string Field,
    string Code,
    ErrorType Type = ErrorType.Validation,
    object? Details = null)
{
    public static Error Validation(IReadOnlyList<ValidationDetail> details) =>
        new(details.Count > 0 ? details[0].Field : string.Empty,
            "validation.failed",
            ErrorType.Validation,
            details);

    public static Error Validation(string field, string code) =>
        new(field, code, ErrorType.Validation);

    public static Error NotFound(string entity) =>
        new(entity, $"{entity.ToLowerInvariant()}.notFound", ErrorType.NotFound);

    public static Error Conflict(string field, string code) =>
        new(field, code, ErrorType.Conflict);

    public static Error Forbidden(string code = "auth.forbidden") =>
        new(string.Empty, code, ErrorType.Forbidden);

    public static Error Unauthorized(string code = "auth.required") =>
        new(string.Empty, code, ErrorType.Unauthorized);

    public static Error Transient(string code, object? details = null) =>
        new(string.Empty, code, ErrorType.Transient, details);

    public static Error Permanent(string code, object? details = null) =>
        new(string.Empty, code, ErrorType.Permanent, details);

    public static Error Configuration(string code) =>
        new(string.Empty, code, ErrorType.Configuration);

    public static Error Unknown(string code, object? details = null) =>
        new(string.Empty, code, ErrorType.Unknown, details);
}
