using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Resolve one user for the GDPR erase screen (T-0178, audit ADM-H1).
///
/// <para>
/// The erase flow previously ran on identifiers the admin pasted in from
/// outside the app: its "lookup" phase verified nothing and the
/// type-the-email interlock matched the email the admin had just typed,
/// so the only irreversible operation in the system was gated on the
/// operator's own clipboard. This query is the server-side identity that
/// screen now confirms against.
/// </para>
///
/// <para>
/// Scope lock (recorded in the ticket): lookup only — no browse/list
/// surface at MVP. Two admins, low volume, and a list would widen PII
/// exposure for no current operator need.
/// </para>
///
/// <para>
/// An already-erased account still RESOLVES (the query ignores the
/// soft-delete filter) so the UI can distinguish "already erased" from
/// "no such user" — conflating them reported a typo as a completed
/// erasure, a false GDPR-compliance signal (audit ADM-M9).
/// </para>
/// </summary>
public static class LookupAdminUser
{
    /// <summary>Exactly one of <paramref name="UserId"/> / <paramref name="Email"/>.</summary>
    public sealed record Query(string? UserId, string? Email) : IQuery<LookupAdminUserResponse>;

    /// <summary>Globally-unique name (NSwag convention).</summary>
    public sealed record LookupAdminUserResponse(AdminUserLookupDto User);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            // Exactly one selector: neither is unanswerable, both is
            // ambiguous (which one wins would be invisible to the caller).
            RuleFor(q => q)
                .Must(q => !string.IsNullOrWhiteSpace(q.UserId) ^ !string.IsNullOrWhiteSpace(q.Email))
                .WithName("userId")
                .WithErrorCode(BusinessErrorMessage.Required);

            RuleFor(q => q.UserId)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength)
                .When(q => !string.IsNullOrWhiteSpace(q.UserId));

            RuleFor(q => q.Email)
                .MaximumLength(320).WithErrorCode(BusinessErrorMessage.MaxLength)
                .EmailAddress().WithErrorCode(BusinessErrorMessage.InvalidEmailFormat)
                .When(q => !string.IsNullOrWhiteSpace(q.Email));
        }
    }

    public sealed class Handler(IAdminQueries adminQueries)
        : IRequestHandler<Query, BusinessResult<LookupAdminUserResponse>>
    {
        public async Task<BusinessResult<LookupAdminUserResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var user = await adminQueries.LookupUserAsync(
                query.UserId, query.Email, cancellationToken);

            if (user is null)
            {
                // Genuinely no such account. NOT the same as "already
                // erased" — that case resolves above with DeactivatedAt set.
                return BusinessResult.Failure<LookupAdminUserResponse>(
                    Error.NotFound("user", BusinessErrorMessage.UserNotFound));
            }

            return BusinessResult.Success(new LookupAdminUserResponse(user));
        }
    }
}
