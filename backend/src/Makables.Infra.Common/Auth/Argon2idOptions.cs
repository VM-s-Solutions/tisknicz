namespace Makables.Infra.Common.Auth;

/// <summary>
/// Argon2id parameters per ADR 0012 §Password policy — the configuration
/// OWASP's Password Storage Cheat Sheet names for Argon2id: 19 MiB of
/// memory, 2 iterations, 1 degree of parallelism. Reviewed yearly.
/// Values are embedded into the versioned hash string, so hashes written
/// under an earlier policy stay verifiable and
/// <see cref="Makables.Core.Domain.Identity.IPasswordHasher.NeedsRehash"/>
/// migrates them on the owner's next successful login.
/// </summary>
/// <remarks>
/// The first draft used 64 MiB / t=3, chosen against a "~100 ms per hash"
/// target that the pure-managed Konscious implementation never met: a
/// warm <c>POST /api/v1/auth/login</c> on the dev B2 plan measured
/// 1.55–1.75 s, and because Argon2id pins a core for that whole time,
/// every login also stalled the five other runtimes sharing the plan.
/// The OWASP configuration is roughly a fifth of that work while staying
/// on the documented recommendation rather than below it.
/// </remarks>
public sealed class Argon2idOptions
{
    public const string SectionName = "Auth:Argon2id";

    // Setters are present (not init-only) so the .NET Configuration
    // Binder can populate from IConfiguration. Consumers MUST treat the
    // bound instance as read-only.
    /// <summary>Memory in KiB. ADR default 19 MiB = 19456 KiB (OWASP minimum for Argon2id).</summary>
    public int MemorySizeKib { get; set; } = 19456;

    /// <summary>Iterations (time cost). ADR default 2, paired with the 19 MiB memory cost.</summary>
    public int Iterations { get; set; } = 2;

    /// <summary>Parallelism (lanes). ADR default 1.</summary>
    public int DegreeOfParallelism { get; set; } = 1;

    /// <summary>Salt length in bytes. 16 is the Argon2 spec recommendation.</summary>
    public int SaltSizeBytes { get; set; } = 16;

    /// <summary>Output hash length in bytes. 32 yields a 256-bit derived key.</summary>
    public int HashSizeBytes { get; set; } = 32;
}
