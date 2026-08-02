namespace Makables.Infra.Clients.Dev;

/// <summary>
/// Configuration for the non-production payment bypass
/// (<see cref="DevPaymentProvider"/>). Bound from the <c>Payments:Dev</c>
/// configuration section.
///
/// <para>
/// <b>Fail closed.</b> <see cref="Enabled"/> defaults to <c>false</c>, so
/// an environment that simply does not mention the section keeps the real
/// gateway. The flag must be set explicitly per environment — it is NOT
/// derived from <c>ASPNETCORE_ENVIRONMENT</c>, because the deployed dev
/// App Services run with <c>ASPNETCORE_ENVIRONMENT=Production</c>
/// (see <c>infra/bicep/modules/app-service.bicep</c>) and an
/// environment-name check would therefore be both wrong on dev and one
/// typo away from being wrong on production.
/// </para>
/// </summary>
public sealed class DevPaymentOptions
{
    public const string SectionName = "Payments:Dev";

    /// <summary>
    /// Master switch. When true the <c>dev</c> payment provider is
    /// registered, <c>PaymentProviderFactory</c> selects it ahead of the
    /// country's configured provider, and the Customer host exposes the
    /// dev-payment confirm endpoint. Never set this in production.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Base URL the customer's BROWSER is sent to in place of the real
    /// gateway. <see cref="DevPaymentProvider"/> appends
    /// <c>/api/v1/orders/{orderId}/dev-payment/confirm</c>.
    ///
    /// <para>
    /// Whatever this resolves to must be SAME-SITE with the page the
    /// customer is on, because the confirm endpoint is <c>[Authorize]</c>d
    /// and the ADR 0012 session cookies are <c>SameSite=Strict</c> — a
    /// cross-site top-level navigation arrives with no cookie and 401s.
    /// Two shapes are therefore accepted:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>Absolute</b> (<c>http://localhost:5001</c>) —
    ///     for local dev, where the frontend on <c>localhost:3000</c> and the
    ///     Customer host on <c>localhost:5001</c> are the same site (ports do
    ///     not split a site) and there is no proxy rewrite.</description></item>
    ///   <item><description><b>Origin-relative</b> (<c>/api-proxy/customer</c>) —
    ///     for deployed environments, where the API sits on a sibling
    ///     <c>*.azurewebsites.net</c> name that IS cross-site and everything
    ///     must go through the T-0153 same-origin proxy rewrite in
    ///     <c>frontend/next.config.ts</c>. Leaving it relative means the
    ///     browser resolves it against whichever hostname the tester
    ///     actually browsed (custom domain or default App Service name),
    ///     so the hop stays same-origin either way.</description></item>
    /// </list>
    /// </summary>
    public string ConfirmBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Shared validity rule for <see cref="ConfirmBaseUrl"/>, used both by
    /// the startup validator and by <see cref="DevPaymentProvider"/> at
    /// call time. Protocol-relative values (<c>//host/path</c>) are
    /// rejected: they look relative but jump to another origin.
    /// </summary>
    public static bool IsValidConfirmBaseUrl(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        if (trimmed.StartsWith('/'))
        {
            return !trimmed.StartsWith("//", StringComparison.Ordinal);
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
