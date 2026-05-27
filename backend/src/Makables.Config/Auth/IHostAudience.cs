namespace Makables.Config.Auth;

/// <summary>
/// Identifies which Web host (<c>customer</c>/<c>maker</c>/<c>admin</c>/<c>public</c>)
/// is serving the current request. Read by shared controllers (e.g.
/// <c>AuthController</c>) so they can:
///
/// <list type="bullet">
///   <item><description>name session cookies per host (<see cref="AuthCookies"/>);</description></item>
///   <item><description>route audience-aware commands (e.g. <c>Login.Command.Audience</c>);</description></item>
///   <item><description>refuse cross-host operations (e.g. a register-customer call landing on the maker host).</description></item>
/// </list>
///
/// Registered as a singleton by each host's <c>Program.cs</c> via
/// <c>AddMakablesHostAudience(MakablesHosts.X)</c>.
/// </summary>
public interface IHostAudience
{
    string Value { get; }
}

internal sealed class HostAudience(string value) : IHostAudience
{
    public string Value { get; } = string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("Host audience required.", nameof(value))
        : value;
}
