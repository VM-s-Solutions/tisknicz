using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Categories;

/// <summary>
/// Admin hides a category from new-product forms (US-admin-0013 AC-3).
/// Existing products in the category remain — they keep their FK by
/// primary key. The public catalog hides the filter chip (driven by
/// the soft-delete query filter at the catalog query layer).
///
/// <para>
/// Audited via <c>AdminAuditPipelineBehavior</c>. Host wiring MUST
/// gate with <c>[Authorize(Roles = "Admin")]</c>.
/// </para>
/// </summary>
public static class DeactivateCategory
{
    public sealed record Command(string CategoryId, string? Notes)
        : ICommand, IAdminAuditableCommand
    {
        public string ActionCode => "category.deactivate";
        public string TargetEntity => "category";
        public string TargetId => CategoryId;
    }

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.CategoryId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            When(c => c.Notes is not null, () =>
            {
                RuleFor(c => c.Notes!)
                    .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
            });
        }
    }

    public sealed class Handler(
        ICategoryRepository categories,
        IUserSessionProvider session,
        IClock clock)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var adminUserId = session.GetUserId();
            if (string.IsNullOrEmpty(adminUserId))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            // ICategoryRepository.GetByIdAsync is filtered by the global
            // soft-delete query filter, so an already-deactivated category
            // surfaces as NotFound. Same shape as DeactivateMaker.
            var category = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
            if (category is null)
            {
                return BusinessResult.Failure(Error.NotFound("category"));
            }

            category.MarkDeactivated(adminUserId, clock.UtcNow);

            return BusinessResult.Success();
        }
    }
}
