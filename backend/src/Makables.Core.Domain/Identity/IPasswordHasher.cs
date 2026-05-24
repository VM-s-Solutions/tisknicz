namespace Makables.Core.Domain.Identity;

/// <summary>
/// Password hashing boundary. Implementation lives in
/// <c>Makables.Infra.Common.Auth</c>; handlers depend on this interface only
/// (ADR 0001 layering). Per ADR 0012 §Password policy: Argon2id, versioned
/// storage format, ~100 ms target on App Service B2.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Compute a versioned Argon2id hash of <paramref name="password"/>.
    /// Output shape: <c>argon2id$v=19$m=&lt;mem&gt;,t=&lt;iters&gt;,p=&lt;par&gt;$&lt;salt-b64&gt;$&lt;hash-b64&gt;</c>.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Constant-time comparison of <paramref name="password"/> against a
    /// previously-stored versioned hash. Returns false on any parse failure.
    /// </summary>
    bool Verify(string password, string storedHash);

    /// <summary>
    /// True when <paramref name="storedHash"/> was produced with parameters
    /// that no longer match the current policy (algorithm bumped, memory
    /// raised, iterations changed, etc.). The login handler re-hashes on
    /// successful verification and persists the new hash transparently.
    /// </summary>
    bool NeedsRehash(string storedHash);

    /// <summary>
    /// A throwaway hash produced with the CURRENT parameters that
    /// <see cref="Verify"/> always rejects. Used by the login flow to
    /// equalize Argon2 latency on the unknown-email branch so an attacker
    /// can't enumerate emails by response time (reviewer T-0022 B-2).
    /// Implementations cache the value so the cost is paid once per
    /// process, not once per failed login.
    /// </summary>
    string DummyHashForTimingEqualization { get; }
}
