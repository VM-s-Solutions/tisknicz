namespace Makables.Infra.Clients.Apple;

/// <summary>
/// Thrown by <see cref="AppleOAuthClient"/> (and
/// <see cref="AppleClientSecretSigner"/>) for any unrecoverable failure
/// (network error, invalid code, ID-token validation failure, client
/// secret signing failure). Caught by the use-case layer which maps to
/// a <see cref="Makables.Core.Domain.Common.BusinessResult"/> failure.
/// Mirrors <c>Google.GoogleOAuthException</c>.
/// </summary>
public sealed class AppleOAuthException : Exception
{
    public AppleOAuthException(string message) : base(message) { }
    public AppleOAuthException(string message, Exception inner) : base(message, inner) { }
}
