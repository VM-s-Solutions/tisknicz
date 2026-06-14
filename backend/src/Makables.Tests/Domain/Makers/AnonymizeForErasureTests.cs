using FluentAssertions;
using Makables.Core.Domain.Makers;

namespace Makables.Tests.Domain.Makers;

/// <summary>
/// Pure-logic transform: <see cref="Maker.AnonymizeForErasure"/> scrubs
/// the maker PII in place, RETAINS the IČO + bank account (legal/payout
/// obligation), and flags the row as a legally-retained tombstone. Written
/// red-first per the TDD policy (T-0110, locked Q-A erasure matrix).
/// </summary>
public sealed class AnonymizeForErasureTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 14, 10, 0, 0, TimeSpan.Zero);

    private static Maker BuildMaker() =>
        Maker.Create(
            id: "maker-1",
            userId: "user-1",
            registrationNumber: "27074358",
            vatId: "CZ27074358",
            companyName: "Avast s.r.o.",
            legalForm: "Společnost s ručením omezeným",
            registeredAddressId: "addr-1",
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: Now,
            snapshotIsStale: false,
            countryCode: "CZ",
            slug: "avast")
        .UpdateProfile(bio: "Tiskneme od roku 2010", bankAccount: "123456789/0100",
            personalPickupEnabled: true, pickupNote: "Zazvoňte na Avast");

    [Fact]
    public void AnonymizeForErasure_scrubs_PII_keeps_ICO_and_bank_and_sets_legal_flag()
    {
        var maker = BuildMaker();

        maker.AnonymizeForErasure();

        maker.CompanyName.Should().Be("Anonymized");
        maker.Bio.Should().Be("Anonymized");
        maker.PickupNote.Should().Be("Anonymized");
        maker.VatId.Should().BeNull("the VAT id is contact PII, scrubbed");

        // Retained for tax/payout records.
        maker.RegistrationNumber.Should().Be("27074358");
        maker.BankAccount.Should().Be("123456789/0100");
        maker.IsRetainedForLegal.Should().BeTrue();
    }

    [Fact]
    public void AnonymizeForErasure_is_idempotent()
    {
        var maker = BuildMaker();

        maker.AnonymizeForErasure();
        var act = () => maker.AnonymizeForErasure();

        act.Should().NotThrow();
        maker.RegistrationNumber.Should().Be("27074358");
        maker.BankAccount.Should().Be("123456789/0100");
        maker.IsRetainedForLegal.Should().BeTrue();
        maker.CompanyName.Should().Be("Anonymized");
    }
}
