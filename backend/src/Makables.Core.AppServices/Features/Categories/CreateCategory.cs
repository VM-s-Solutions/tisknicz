using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Categories;

/// <summary>
/// Admin creates a new category (US-admin-0013 AC-1). Audited via
/// <c>AdminAuditPipelineBehavior</c>. The category is initially active
/// and immediately appears in both the public filter list and maker
/// product-creation forms.
///
/// <para>
/// Slug semantics: if the caller supplies one it's used verbatim (admin
/// override); otherwise <see cref="Category.Slugify"/> derives one from
/// the name (diacritics stripped). The handler pre-checks
/// <see cref="ICategoryRepository.SlugExistsAsync"/> before the add;
/// a TOCTOU race surfaces as the same
/// <see cref="BusinessErrorMessage.CategorySlugAlreadyExists"/> via the
/// <c>UniqueConstraintTranslator</c>.
/// </para>
///
/// <para>
/// <b>Authorization.</b> The handler does NOT verify the caller is an
/// admin. The host that wires this controller MUST gate the endpoint
/// with <c>[Authorize(Roles = "Admin")]</c>. Same shape T-0034
/// reviewer M-1 documented for the maker-admin commands.
/// </para>
/// </summary>
public static class CreateCategory
{
    /// <summary>
    /// <paramref name="Id"/> is pre-allocated by the controller (via
    /// <see cref="IIdGenerator"/>) so the <c>AdminAuditPipelineBehavior</c>
    /// has a stable <c>TargetId</c> to write into the audit row both
    /// before and after the handler runs. The before-snapshot lookup
    /// returns null (the row doesn't exist yet), which is the expected
    /// shape for a Create — the after-snapshot captures the row.
    /// </summary>
    public sealed record Command(
        string Id,
        string Name,
        string? Slug,
        string? Icon,
        string? Description,
        int SortOrder,
        string CountryCode,
        string? Notes)
        : ICommand<Response>, IAdminAuditableCommand
    {
        public string ActionCode => "category.create";
        public string TargetEntity => "category";
        public string TargetId => Id;
    }

    public sealed record Response(string Id, string Slug);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Id)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Name)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(100).WithErrorCode(BusinessErrorMessage.MaxLength);

            When(c => !string.IsNullOrEmpty(c.Slug), () =>
            {
                RuleFor(c => c.Slug!)
                    .MaximumLength(100).WithErrorCode(BusinessErrorMessage.MaxLength);
            });

            When(c => c.Icon is not null, () =>
            {
                RuleFor(c => c.Icon!)
                    .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);
            });

            When(c => c.Description is not null, () =>
            {
                RuleFor(c => c.Description!)
                    .MaximumLength(500).WithErrorCode(BusinessErrorMessage.MaxLength);
            });

            RuleFor(c => c.CountryCode)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(2).WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

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
        : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            // Resolve slug now so we can pre-check uniqueness BEFORE
            // building the aggregate. Mirrors the RegisterMaker shape:
            // no aggregate is added to the change tracker until all
            // gates pass.
            var resolvedSlug = string.IsNullOrWhiteSpace(command.Slug)
                ? Category.Slugify(command.Name)
                : command.Slug.Trim();

            if (resolvedSlug.Length == 0)
            {
                return BusinessResult.Failure<Response>(
                    Error.Validation(nameof(command.Slug), BusinessErrorMessage.Required));
            }

            if (await categories.SlugExistsAsync(resolvedSlug, cancellationToken))
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("slug", BusinessErrorMessage.CategorySlugAlreadyExists));
            }

            var category = Category.Create(
                id: command.Id,
                name: command.Name,
                slug: resolvedSlug,
                icon: command.Icon,
                description: command.Description,
                sortOrder: command.SortOrder,
                countryCode: command.CountryCode);

            categories.Add(category);

            return BusinessResult.Success(new Response(category.Id, category.Slug));
        }
    }
}
