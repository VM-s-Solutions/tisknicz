using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Email;

/// <summary>
/// Resolves the country-specific <see cref="IEmailProvider"/> by reading
/// <c>CountryConfiguration.DefaultEmailProvider</c>. Per ADR 0008 /
/// patterns §A.15 (keyed services + provider factory) — T-0124 migrates
/// the SendGrid adapter from direct DI onto the keyed pattern T-0065
/// introduced for payments.
///
/// <para>
/// <b>Send-path note (MVP):</b> the outbox contract carries a
/// <c>LanguageCode</c> but no recipient country, so
/// <c>EmailSendService</c> still consumes the unkeyed
/// <see cref="IEmailProvider"/> alias (registered as a delegate onto the
/// keyed "sendgrid" instance). When multi-country launch adds a
/// <c>CountryCode</c> to the email payloads (next to LanguageCode, per
/// T-0028's precedent), the send path switches to this factory without
/// re-plumbing DI.
/// </para>
///
/// <para>
/// Failure modes:
/// <list type="bullet">
///   <item><description><see cref="BusinessErrorMessage.CountryConfigurationNotFound"/>
///     when the country code itself isn't seeded.</description></item>
///   <item><description><see cref="BusinessErrorMessage.EmailProviderNotRegistered"/>
///     when the country's <c>DefaultEmailProvider</c> code does not match
///     any registered keyed <see cref="IEmailProvider"/>.</description></item>
/// </list>
/// </para>
/// </summary>
public interface IEmailProviderFactory
{
    Task<BusinessResult<IEmailProvider>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken);
}
