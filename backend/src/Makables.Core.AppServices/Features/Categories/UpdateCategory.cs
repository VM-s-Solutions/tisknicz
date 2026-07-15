using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Categories;

/// <summary>
/// Admin renames or re-orders a category (US-admin-0013 AC-2). The
/// slug is NOT mutable — that's a per-category invariant for SEO and
/// existing public URLs. Products keep their FK by primary key, which
/// is unrelated to slug either way.
///
/// <para>
/// Audited via <c>AdminAuditPipelineBehavior</c>. Host wiring MUST
/// gate with <c>[Authorize(Roles = "Admin")]</c>.
/// </para>
/// </summary>
public static class UpdateCategory
{
    public sealed record Command(
        string CategoryId,
        string Name,
        string? Icon,
        string? Description,
        int SortOrder,
        string? Notes)
        : ICommand, IAdminAuditableCommand
    {
        public string ActionCode => "category.update";
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

            RuleFor(c => c.Name)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(100).WithErrorCode(BusinessErrorMessage.MaxLength)
                .Must(n => !ProhibitedContent.ContainsProhibitedTerm(n))
                .WithErrorCode(BusinessErrorMessage.CategoryNameNotAllowed);

            When(c => c.Icon is not null, () =>
            {
                RuleFor(c => c.Icon!)
                    .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);
            });

            When(c => c.Description is not null, () =>
            {
                RuleFor(c => c.Description!)
                    .MaximumLength(500).WithErrorCode(BusinessErrorMessage.MaxLength)
                    .Must(d => !ProhibitedContent.ContainsProhibitedTerm(d))
                    .WithErrorCode(BusinessErrorMessage.CategoryNameNotAllowed);
            });

            When(c => c.Notes is not null, () =>
            {
                RuleFor(c => c.Notes!)
                    .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
            });
        }
    }

    public sealed class Handler(
        ICategoryRepository categories,
        IUserSessionProvider session)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            var category = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
            if (category is null)
            {
                return BusinessResult.Failure(Error.NotFound("category"));
            }

            category.UpdateMetadata(
                name: command.Name,
                icon: command.Icon,
                description: command.Description,
                sortOrder: command.SortOrder);

            return BusinessResult.Success();
        }
    }
}
