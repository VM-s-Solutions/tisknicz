namespace Makables.Core.Domain.Common;

/// <summary>
/// A single field-level validation failure. Kept Infra-free in
/// <c>Core.Domain</c>; the <c>ValidationPipelineBehavior</c> in
/// <c>Core.AppServices</c> maps FluentValidation's <c>ValidationFailure</c>
/// to this record before constructing a <see cref="BusinessResult"/>.
/// </summary>
public sealed record ValidationDetail(string Field, string Code, string Message);
