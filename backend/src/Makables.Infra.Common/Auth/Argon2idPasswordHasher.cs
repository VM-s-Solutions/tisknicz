using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Options;

namespace Makables.Infra.Common.Auth;

/// <summary>
/// Argon2id implementation of <see cref="IPasswordHasher"/>. Storage format
/// per ADR 0012 §Password policy:
/// <code>argon2id$v=19$m=&lt;mem&gt;,t=&lt;iters&gt;,p=&lt;par&gt;$&lt;salt-b64&gt;$&lt;hash-b64&gt;</code>
///
/// The version prefix encodes the parameters used to produce the hash so
/// migrations bump the policy without breaking existing hashes. The
/// <see cref="NeedsRehash"/> probe tells the login handler when to
/// transparently re-hash on successful verification.
/// </summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private readonly Argon2idOptions _options;

    public Argon2idPasswordHasher(IOptions<Argon2idOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(_options.SaltSizeBytes);
        var hashBytes = ComputeHash(password, salt, _options.MemorySizeKib, _options.Iterations,
            _options.DegreeOfParallelism, _options.HashSizeBytes);

        return Format(
            memoryKib: _options.MemorySizeKib,
            iterations: _options.Iterations,
            parallelism: _options.DegreeOfParallelism,
            salt: salt,
            hash: hashBytes);
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;
        if (!TryParse(storedHash, out var parsed)) return false;

        var computed = ComputeHash(
            password, parsed.Salt,
            parsed.MemoryKib, parsed.Iterations, parsed.Parallelism,
            parsed.Hash.Length);

        // Constant-time comparison defeats timing oracles.
        return CryptographicOperations.FixedTimeEquals(computed, parsed.Hash);
    }

    public bool NeedsRehash(string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return true;
        if (!TryParse(storedHash, out var parsed)) return true;

        return parsed.MemoryKib != _options.MemorySizeKib
            || parsed.Iterations != _options.Iterations
            || parsed.Parallelism != _options.DegreeOfParallelism
            || parsed.Hash.Length != _options.HashSizeBytes;
    }

    private static byte[] ComputeHash(string password, byte[] salt, int memoryKib, int iterations, int parallelism, int hashSize)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(hashSize);
    }

    private static string Format(int memoryKib, int iterations, int parallelism, byte[] salt, byte[] hash) =>
        $"argon2id$v=19$m={memoryKib},t={iterations},p={parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";

    private static bool TryParse(string storedHash, out ParsedHash parsed)
    {
        parsed = default;
        // Shape: argon2id$v=19$m=<m>,t=<t>,p=<p>$<saltB64>$<hashB64>
        var parts = storedHash.Split('$');
        if (parts.Length != 5) return false;
        if (parts[0] != "argon2id") return false;
        if (parts[1] != "v=19") return false;

        // Parameters block: m=<m>,t=<t>,p=<p>
        var paramPairs = parts[2].Split(',');
        if (paramPairs.Length != 3) return false;
        if (!TryParsePrefixedInt(paramPairs[0], "m=", out var memoryKib)) return false;
        if (!TryParsePrefixedInt(paramPairs[1], "t=", out var iterations)) return false;
        if (!TryParsePrefixedInt(paramPairs[2], "p=", out var parallelism)) return false;

        byte[] salt;
        byte[] hash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            hash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        parsed = new ParsedHash(memoryKib, iterations, parallelism, salt, hash);
        return true;
    }

    private static bool TryParsePrefixedInt(string segment, string prefix, out int value)
    {
        value = 0;
        if (!segment.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return int.TryParse(segment.AsSpan(prefix.Length), out value);
    }

    private readonly record struct ParsedHash(int MemoryKib, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}
