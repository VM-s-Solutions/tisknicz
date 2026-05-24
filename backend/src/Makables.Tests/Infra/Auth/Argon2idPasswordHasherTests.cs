using FluentAssertions;
using Makables.Core.Domain.Identity;
using Makables.Infra.Common.Auth;
using Microsoft.Extensions.Options;

namespace Makables.Tests.Infra.Auth;

public class Argon2idPasswordHasherTests
{
    // Light parameters keep the test suite fast (~10–20 ms per hash) without
    // changing the algorithm's correctness. Production runs at the ADR's
    // 64 MiB / 3 iter settings.
    private static readonly Argon2idOptions FastOptions = new()
    {
        MemorySizeKib = 8192,   // 8 MiB
        Iterations = 1,
        DegreeOfParallelism = 1,
        SaltSizeBytes = 16,
        HashSizeBytes = 32,
    };

    private static IPasswordHasher CreateHasher(Argon2idOptions? opts = null) =>
        new Argon2idPasswordHasher(Options.Create(opts ?? FastOptions));

    [Fact]
    public void Hash_then_Verify_returns_true_for_the_same_password()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("correct horse battery staple");

        hasher.Verify("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_returns_false_for_a_wrong_password()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("correct horse battery staple");

        hasher.Verify("Tr0ub4dor&3", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_produces_different_output_each_call_due_to_random_salt()
    {
        var hasher = CreateHasher();
        var h1 = hasher.Hash("same-input");
        var h2 = hasher.Hash("same-input");

        h1.Should().NotBe(h2, "each Hash invocation generates a fresh salt");
        // Both must still verify against the same plaintext.
        hasher.Verify("same-input", h1).Should().BeTrue();
        hasher.Verify("same-input", h2).Should().BeTrue();
    }

    [Fact]
    public void Hash_starts_with_the_versioned_argon2id_prefix()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("anything");

        hash.Should().StartWith("argon2id$v=19$m=8192,t=1,p=1$");
        hash.Split('$').Should().HaveCount(5, "shape is argon2id$v=19$params$salt$hash");
    }

    [Fact]
    public void Verify_returns_false_when_stored_hash_is_malformed()
    {
        var hasher = CreateHasher();

        hasher.Verify("anything", "not-a-hash").Should().BeFalse();
        hasher.Verify("anything", "argon2id$v=19$m=8192,t=1,p=1$onlyfour").Should().BeFalse();
        hasher.Verify("anything", "argon2id$v=19$m=8192,t=1,p=1$%not-base64%$%not-base64%").Should().BeFalse();
    }

    [Fact]
    public void Verify_returns_false_for_empty_password_or_hash()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("pw");

        hasher.Verify("", hash).Should().BeFalse();
        hasher.Verify("pw", "").Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_returns_true_when_parameters_have_been_bumped()
    {
        // Hash with low memory; "current policy" then asks for more.
        var oldHasher = CreateHasher(FastOptions);
        var oldHash = oldHasher.Hash("pw");

        var bumpedHasher = CreateHasher(new Argon2idOptions
        {
            MemorySizeKib = 16384,
            Iterations = FastOptions.Iterations,
            DegreeOfParallelism = FastOptions.DegreeOfParallelism,
            HashSizeBytes = FastOptions.HashSizeBytes,
        });

        bumpedHasher.NeedsRehash(oldHash).Should().BeTrue();
        // But verification of the old hash still succeeds — the parameters
        // for the stored hash are embedded in its prefix.
        bumpedHasher.Verify("pw", oldHash).Should().BeTrue();
    }

    [Fact]
    public void NeedsRehash_returns_false_for_a_freshly_produced_hash()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("pw");
        hasher.NeedsRehash(hash).Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_returns_true_for_malformed_or_empty_input()
    {
        var hasher = CreateHasher();
        hasher.NeedsRehash("").Should().BeTrue();
        hasher.NeedsRehash("not-a-hash").Should().BeTrue();
    }
}
