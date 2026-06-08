using Microsoft.Extensions.Options;

namespace Makables.Infra.Clients.Packeta;

/// <summary>
/// Startup validator for <see cref="PacketaOptions"/>. Per ADR 0017 +
/// T-0070 — a typo'd <c>Packeta:ApiKey</c> or <c>Packeta:PublicWidgetKey</c>
/// silently fails every ship call until the customer complains; fail at
/// boot instead so deploy logs surface the misconfig immediately.
/// </summary>
public sealed class PacketaOptionsValidator : IValidateOptions<PacketaOptions>
{
    public ValidateOptionsResult Validate(string? name, PacketaOptions options)
    {
        if (options is null) return ValidateOptionsResult.Fail("Packeta section is missing.");
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            errors.Add("Packeta:ApiKey is required.");
        if (string.IsNullOrWhiteSpace(options.PublicWidgetKey))
            errors.Add("Packeta:PublicWidgetKey is required.");
        if (string.IsNullOrWhiteSpace(options.SenderLabel))
            errors.Add("Packeta:SenderLabel is required.");

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Packeta:BaseUrl must be an absolute http(s) URI.");
        }

        if (!Uri.TryCreate(options.WidgetScriptUrl, UriKind.Absolute, out var widgetUri)
            || widgetUri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Packeta:WidgetScriptUrl must be an absolute https URI.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
