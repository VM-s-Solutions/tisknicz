using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Makers.Validators;
using MediatR;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Maker self-service profile patch (US-maker-0003). Per AC-2 the
/// ARES-snapshot fields (<c>CompanyName</c>, <c>IČO</c>,
/// <c>RegisteredAddress</c>, <c>LegalForm</c>, <c>DIČ</c>) are NOT in
/// the command shape — they're read-only for makers; only an admin
/// can change them via <see cref="RefreshMakerFromAres"/>.
///
/// <para>
/// Field semantics:
/// <list type="bullet">
///   <item><description><c>null</c> — leave the field unchanged.</description></item>
///   <item><description>empty string — clear the field (for optional values).</description></item>
///   <item><description>any other value — replace.</description></item>
/// </list>
/// </para>
///
/// <para>
/// The handler resolves the target Maker from
/// <see cref="IUserSessionProvider.GetUserId"/> — NEVER from a request
/// param — to prevent IDOR. The maker cannot patch any other maker's
/// profile from this command shape.
/// </para>
/// </summary>
public static class UpdateMakerProfile
{
    public sealed record Command(
        string? Bio,
        string? BankAccount,
        bool? PersonalPickupEnabled,
        string? PickupNote)
        : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            // Bio: only enforce length here; null means "leave alone".
            When(c => c.Bio is not null, () =>
            {
                RuleFor(c => c.Bio!.Length)
                    .LessThanOrEqualTo(500)
                    .WithErrorCode(BusinessErrorMessage.MaxLength);
            });

            // BankAccount: format check via the Czech ČNB rule. Empty
            // string is allowed (clears the value); a non-empty string
            // must pass the validator.
            When(c => !string.IsNullOrEmpty(c.BankAccount), () =>
            {
                RuleFor(c => c.BankAccount!)
                    .Must(CzechBankAccountValidator.IsValid)
                    .WithErrorCode(BusinessErrorMessage.InvalidBankAccountFormat);
            });

            When(c => c.PickupNote is not null, () =>
            {
                RuleFor(c => c.PickupNote!.Length)
                    .LessThanOrEqualTo(500)
                    .WithErrorCode(BusinessErrorMessage.MaxLength);
            });
        }
    }

    public sealed class Handler(
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure(Error.NotFound("maker"));
            }

            maker.UpdateProfile(
                bio: command.Bio,
                bankAccount: command.BankAccount,
                personalPickupEnabled: command.PersonalPickupEnabled,
                pickupNote: command.PickupNote);

            return BusinessResult.Success();
        }
    }
}
