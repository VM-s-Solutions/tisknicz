using FluentAssertions;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Makers;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Maker;

/// <summary>
/// T-0140 — pins the SetMakerFeeOverride admin command (US-admin-0018).
/// Covers AC-1 (set), AC-3 (no-override math is out of this handler's
/// scope — pinned instead in PricingServiceTests), AC-4 (clear), AC-5
/// (negative/ceiling rejection), AC-7 (fail-closed auth), AC-8 (the
/// mutator itself never touches historical orders — the handler's job
/// stops at the Maker aggregate; order immutability is a property of
/// PricingService only reading the CURRENT override at order-creation
/// time, asserted in the integration test).
/// </summary>
public class SetMakerFeeOverrideHandlerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly ICountryConfigurationRepository _configs = Substitute.For<ICountryConfigurationRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly SetMakerFeeOverride.Handler _sut;

    public SetMakerFeeOverrideHandlerTests()
    {
        _session.GetUserId().Returns("admin-1");
        _sut = new SetMakerFeeOverride.Handler(_makers, _configs, _session);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker(int? feeRateOverrideBp = null)
    {
        var maker = Makables.Core.Domain.Makers.Maker.Create(
            id: "maker-1",
            userId: "user-1",
            registrationNumber: "27074358",
            vatId: null,
            companyName: "Avast s.r.o.",
            legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt,
            snapshotIsStale: false,
            countryCode: "CZ");
        maker.SetFeeRateOverride(feeRateOverrideBp);
        return maker;
    }

    private static CountryConfiguration CzConfig(int platformFeeRateBp = 700) =>
        CountryConfiguration.Create(
            countryId: "CZ",
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "resend",
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000",
            platformFeeRateBp: platformFeeRateBp);

    [Fact]
    public async Task AC1_sets_an_override_on_a_maker_with_no_prior_override()
    {
        var maker = ExistingMaker(feeRateOverrideBp: null);
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(CzConfig(platformFeeRateBp: 700));

        var result = await _sut.Handle(
            new SetMakerFeeOverride.Command("maker-1", 350, "Loyal maker, 2 years active"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.FeeRateOverrideBp.Should().Be(350);
    }

    [Fact]
    public async Task AC3_no_override_value_leaves_FeeRateOverrideBp_null_and_skips_country_config_lookup()
    {
        // Submitting null (no override requested) never needs the ceiling
        // check, so the country-config repo is never called.
        var maker = ExistingMaker(feeRateOverrideBp: null);
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(
            new SetMakerFeeOverride.Command("maker-1", null, "No change needed"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.FeeRateOverrideBp.Should().BeNull();
        await _configs.DidNotReceive().GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_clears_a_previously_set_override()
    {
        var maker = ExistingMaker(feeRateOverrideBp: 350);
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(
            new SetMakerFeeOverride.Command("maker-1", null, "No longer applicable"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.FeeRateOverrideBp.Should().BeNull();
    }

    [Fact]
    public async Task AC5_rejects_a_negative_override_before_touching_the_repository()
    {
        // Negative rejection is a Validator-level (pure shape) rule per
        // T-0140 — MediatR pipeline wiring is exercised by integration
        // tests; here we assert the Validator directly.
        var validator = new SetMakerFeeOverride.Validator();

        var result = await validator.ValidateAsync(
            new SetMakerFeeOverride.Command("maker-1", -1, "Bad input"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.MinValue);
    }

    [Fact]
    public async Task AC5_rejects_an_override_exceeding_the_country_default()
    {
        var maker = ExistingMaker(feeRateOverrideBp: null);
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(CzConfig(platformFeeRateBp: 700));

        var result = await _sut.Handle(
            new SetMakerFeeOverride.Command("maker-1", 800, "Trying to exceed the cap"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(BusinessErrorMessage.MakerFeeOverrideExceedsCountryDefault);
        maker.FeeRateOverrideBp.Should().BeNull();
    }

    [Fact]
    public async Task AC5_accepts_an_override_exactly_equal_to_the_country_default()
    {
        // "≤ country default, never above" per BA lock — equal is allowed.
        var maker = ExistingMaker(feeRateOverrideBp: null);
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>()).Returns(maker);
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(CzConfig(platformFeeRateBp: 700));

        var result = await _sut.Handle(
            new SetMakerFeeOverride.Command("maker-1", 700, "Match the country default exactly"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.FeeRateOverrideBp.Should().Be(700);
    }

    [Fact]
    public async Task Returns_NotFound_when_maker_is_missing()
    {
        _makers.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(
            new SetMakerFeeOverride.Command("missing", 350, "reason"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task AC7_returns_Unauthorized_when_session_has_no_user_and_persists_nothing()
    {
        // Fail-closed shape — host-level [Authorize] should make this
        // unreachable, but attributing a fee-rate change to "system" via
        // the audit pipeline would mask a misconfigured endpoint.
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new SetMakerFeeOverride.Command("maker-1", 350, "reason"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _makers.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AC8_changing_the_override_does_not_reach_into_order_history()
    {
        // The handler's mutation surface is scoped to the Maker aggregate
        // only — it never touches Order rows. AC-8 (historical orders'
        // PlatformFeeMinor snapshot stays unchanged) is a property of
        // PricingService reading the override at order-creation time, not
        // of this handler — asserted end-to-end by the integration test
        // T-0140 §"priced order snapshots the overridden rate".
        var maker = ExistingMaker(feeRateOverrideBp: 350);
        maker.SetFeeRateOverride(null);
        maker.FeeRateOverrideBp.Should().BeNull();
    }

    [Fact]
    public void Command_carries_admin_audit_metadata()
    {
        var cmd = new SetMakerFeeOverride.Command("maker-1", 350, "loyalty discount");
        cmd.ActionCode.Should().Be("maker.setFeeOverride");
        cmd.TargetEntity.Should().Be("maker");
        cmd.TargetId.Should().Be("maker-1");
        cmd.Notes.Should().Be("loyalty discount");
    }
}
