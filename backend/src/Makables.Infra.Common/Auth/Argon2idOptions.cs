namespace Makables.Infra.Common.Auth;

/// <summary>
/// Argon2id parameters per ADR 0012 §Password policy. Defaults target
/// ~100 ms per hash on App Service B2; reviewed yearly. Values are
/// embedded into the versioned hash string so old hashes remain
/// verifiable after a policy bump.
/// </summary>
public sealed class Argon2idOptions
{
    public const string SectionName = "Auth:Argon2id";

    // Setters are present (not init-only) so the .NET Configuration
    // Binder can populate from IConfiguration. Consumers MUST treat the
    // bound instance as read-only.
    /// <summary>Memory in KiB. ADR default 64 MiB = 65536 KiB.</summary>
    public int MemorySizeKib { get; set; } = 65536;

    /// <summary>Iterations (time cost). ADR default 3.</summary>
    public int Iterations { get; set; } = 3;

    /// <summary>Parallelism (lanes). ADR default 1.</summary>
    public int DegreeOfParallelism { get; set; } = 1;

    /// <summary>Salt length in bytes. 16 is the Argon2 spec recommendation.</summary>
    public int SaltSizeBytes { get; set; } = 16;

    /// <summary>Output hash length in bytes. 32 yields a 256-bit derived key.</summary>
    public int HashSizeBytes { get; set; } = 32;
}
