using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using MediatR;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Admin sets or clears a maker's per-maker loyalty fee-rate override
/// (T-0140 / US-admin-0018): 7% base commission, 3,5% for makers who've
/// cooperated with the platform longer. The criteria for *who* qualifies
/// (dopady §5.2) are explicitly undecided and out of scope — this command
/// is the admin-manual MVP fallback, the same judgment-call shape as
/// <see cref="VerifyMaker"/>.
///
/// <para>
/// Audited via <c>AdminAuditPipelineBehavior</c> — the before/after JSONB
/// snapshot pins the <c>FeeRateOverrideBp</c> flip per ADR 0014 (AC-1,
/// AC-4).
/// </para>
///
/// <para>
/// <b>Discount-only ceiling (AC-5).</b> The submitted value must be
/// non-negative (Validator, pure shape check) AND must not exceed the
/// maker's <c>CountryConfiguration.PlatformFeeRateBp</c> (Handler — this
/// needs the maker's country loaded, so it can't live in a DB-free
/// Validator per this codebase's established convention; see
/// <c>UpdateCountryConfiguration</c>'s provider-registration check for the
/// same shape). Rejecting here means nothing is persisted.
/// </para>
///
/// <para>
/// Submitting <c>null</c> clears the override (AC-4) — pricing for that
/// maker reverts to the country default on the next order. Changing or
/// clearing an override NEVER touches already-priced orders (AC-8): the
/// resolved rate is snapshotted onto the order at creation time by
/// <c>PricingService.ComputeForProductAsync</c>, not read live.
/// </para>
///
/// <para>
/// <b>Authorization.</b> The handler does NOT verify the caller is an
/// admin. The host that wires this controller MUST gate the endpoint with
/// <c>[Authorize(Roles = "Admin")]</c> (or the equivalent JWT-audience
/// scope on <c>Web.Admin</c>). Wiring this on a non-admin host is a
/// privilege-escalation vulnerability — same T-0034 sec reviewer M-1
/// precedent as <see cref="VerifyMaker"/> / <see cref="DeactivateMaker"/>.
/// </para>
/// </summary>
public static class SetMakerFeeOverride
{
    public sealed record Command(string MakerId, int? FeeRateOverrideBp, string Reason)
        : ICommand, IAdminAuditableCommand
    {
        public string ActionCode => "maker.setFeeOverride";
        public string TargetEntity => "maker";
        public string TargetId => MakerId;
        public string? Notes => Reason;
    }

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.MakerId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            // AC-5 (negative half): pure shape check, no DB needed. The
            // country-default ceiling half of AC-5 needs the maker's
            // CountryConfiguration loaded — that runs in the Handler.
            When(c => c.FeeRateOverrideBp.HasValue, () =>
            {
                RuleFor(c => c.FeeRateOverrideBp!.Value)
                    .GreaterThanOrEqualTo(0).WithErrorCode(BusinessErrorMessage.MinValue);
            });

            // Reason is required — mirrors the audited-mutation precedent
            // (RefundOrder, ChangeOrderStateManually) of a mandatory reason
            // for an admin action that changes money math.
            RuleFor(c => c.Reason)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IMakerRepository makers,
        Domain.Configuration.ICountryConfigurationRepository configs,
        IUserSessionProvider session)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            // AC-7: fail-closed when there's no session user. The
            // host-level [Authorize(Roles="Admin")] gate should make this
            // unreachable, but attributing a fee-rate change to "system"
            // via AdminAuditPipelineBehavior would mask a misconfigured
            // endpoint. Matches VerifyMaker / DeactivateMaker (T-0034 sec
            // reviewer m-1).
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            var maker = await makers.GetByIdAsync(command.MakerId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure(Error.NotFound("maker"));
            }

            // AC-5 (ceiling half): the override is a discount only — it
            // must never exceed the maker's country's advertised default
            // rate (Alternatives Considered Option B, rejected for MVP).
            if (command.FeeRateOverrideBp.HasValue)
            {
                var config = await configs.GetByCodeAsync(maker.CountryCode, cancellationToken);
                if (config is null)
                {
                    return BusinessResult.Failure(
                        Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
                }

                if (command.FeeRateOverrideBp.Value > config.PlatformFeeRateBp)
                {
                    return BusinessResult.Failure(
                        Error.Validation(
                            "feeRateOverrideBp",
                            BusinessErrorMessage.MakerFeeOverrideExceedsCountryDefault));
                }
            }

            maker.SetFeeRateOverride(command.FeeRateOverrideBp);

            return BusinessResult.Success();
        }
    }
}
