using FluentAssertions;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Storage;
using Makables.Infra.Database.Privacy;
using Makables.Tests.Infra.Database;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Makables.Tests.Infra.Privacy;

/// <summary>
/// Covers the GDPR erasure step that deletes profile imagery — the one action
/// that makes storing user photographs defensible at all, and which had ZERO
/// coverage before this class (Q-0043). No test anywhere seeded an
/// <c>AvatarBlobPath</c> or a <c>LogoBlobPath</c>, so the
/// <c>if (!string.IsNullOrEmpty(path))</c> guard was always false and the
/// delete loop never executed in CI.
/// </summary>
public class UserDataDeletionServiceBlobTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");

    private static (UserDataDeletionService Sut, IBlobStorageClient Blobs, ILogger<UserDataDeletionService> Logger)
        Build(TestDbHarness h)
    {
        var blobs = Substitute.For<IBlobStorageClient>();
        blobs.DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success());
        var logger = Substitute.For<ILogger<UserDataDeletionService>>();
        return (new UserDataDeletionService(h.Db, blobs, logger), blobs, logger);
    }

    private static User SeedUserWithAvatar(TestDbHarness h, string avatarPath)
    {
        var user = User.Create(
            id: "user-1", email: "u@example.cz", role: UserRole.Maker,
            fullName: "Owner", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: true, confirmedAt: Now);
        user.SetAvatar(avatarPath);
        h.Db.Set<User>().Add(user);
        return user;
    }

    private static void SeedMakerWithLogo(TestDbHarness h, string logoPath)
    {
        h.Db.Set<Address>().Add(Address.Create(
            id: "addr-1", street: "Ulice", houseNumber: "1", city: "Praha",
            zip: "10000", countryCodeIso: "CZ", auditCountryCode: "CZ"));

        var maker = Maker.Create(
            id: "maker-1", userId: "user-1", registrationNumber: "10000001",
            vatId: null, companyName: "Keramika s.r.o.", legalForm: "s.r.o.",
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares", snapshotFetchedAt: Now,
            snapshotIsStale: false, countryCode: "CZ", slug: "keramika");
        maker.SetLogo(logoPath);
        h.Db.Set<Maker>().Add(maker);
    }

    [Fact]
    public async Task Erasure_deletes_the_avatar_and_the_maker_logo_from_profile_images()
    {
        using var h = TestDbHarness.Create();
        SeedUserWithAvatar(h, "cz/avatars/user-1/01J.jpg");
        SeedMakerWithLogo(h, "cz/makers/maker-1/01K.png");
        await h.Db.SaveChangesAsync(default);

        var (sut, blobs, _) = Build(h);
        var result = await sut.EraseAsync("user-1", default);

        result.IsSuccess.Should().BeTrue();
        await blobs.Received(1).DeleteAsync(
            BlobContainer.ProfileImages, "cz/avatars/user-1/01J.jpg", Arg.Any<CancellationToken>());
        await blobs.Received(1).DeleteAsync(
            BlobContainer.ProfileImages, "cz/makers/maker-1/01K.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Erasure_does_not_call_blob_delete_when_there_is_no_imagery()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<User>().Add(User.Create(
            id: "user-1", email: "u@example.cz", role: UserRole.Customer,
            fullName: "Zákazník", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: true, confirmedAt: Now));
        await h.Db.SaveChangesAsync(default);

        var (sut, blobs, _) = Build(h);
        (await sut.EraseAsync("user-1", default)).IsSuccess.Should().BeTrue();

        await blobs.DidNotReceive().DeleteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The Q-0043 defect. <c>DeleteAsync</c> does not throw — it returns
    /// <c>BusinessResult.Failure</c> — and the pointer holding the path is
    /// nulled in the same transaction, so a discarded failure orphaned a
    /// photograph of the subject with nothing recording where it was. The
    /// erasure must still succeed (a lawful erasure cannot be blocked by a
    /// storage hiccup), but the path must survive in the log.
    /// </summary>
    [Fact]
    public async Task A_failed_blob_delete_does_not_abort_the_erasure_but_is_logged_with_the_path()
    {
        using var h = TestDbHarness.Create();
        SeedUserWithAvatar(h, "cz/avatars/user-1/orphan.jpg");
        await h.Db.SaveChangesAsync(default);

        var (sut, blobs, logger) = Build(h);
        blobs.DeleteAsync(BlobContainer.ProfileImages, "cz/avatars/user-1/orphan.jpg", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure(new Error("blob", "storage.deleteFailed", ErrorType.Conflict)));

        var result = await sut.EraseAsync("user-1", default);

        result.IsSuccess.Should().BeTrue("a storage failure must not block a lawful erasure");

        logger.ReceivedCalls()
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 1 && a[0] is LogLevel.Warning)
            .Select(a => a[2]?.ToString() ?? string.Empty)
            .Should().ContainSingle(s => s.Contains("cz/avatars/user-1/orphan.jpg", StringComparison.Ordinal),
                "the log line is the only remaining record of the orphaned blob");
    }
}
