using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Admin cross-tenant invoice list (T-0111 / US-admin-0012). Read-only,
/// <c>Unscoped()</c> (keeps the global soft-delete filter — invoices are
/// never soft-deleted post-issuance). Filters: type, country, recipient,
/// date range (Q-E, no more). No audit row (reads are not audited).
/// </summary>
public static class GetAllInvoices
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(
        int Page,
        int PageSize,
        InvoiceType? Type,
        string? CountryCode,
        string? Recipient,
        DateTimeOffset? DateFrom,
        DateTimeOffset? DateTo) : IQuery<GetAllInvoicesResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record GetAllInvoicesResponse(PagedData<AdminInvoiceListItemDto> Invoices);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Page)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(1, int.MaxValue / MaxPageSize)
                .WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(q => q.PageSize)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(1, MaxPageSize)
                .WithErrorCode(BusinessErrorMessage.MinValue);

            When(q => q.Type.HasValue, () =>
                RuleFor(q => q.Type!.Value)
                    .IsInEnum()
                    .WithErrorCode(BusinessErrorMessage.InvalidEnumValue));

            When(q => !string.IsNullOrEmpty(q.CountryCode), () =>
                RuleFor(q => q.CountryCode!)
                    .Length(2)
                    .WithErrorCode(BusinessErrorMessage.MaxLength));

            // Inverted date range — fast-fail at 400 (T-0080 precedent).
            When(q => q.DateFrom.HasValue && q.DateTo.HasValue, () =>
                RuleFor(q => q.DateFrom!.Value)
                    .LessThanOrEqualTo(q => q.DateTo!.Value)
                    .WithErrorCode(BusinessErrorMessage.MinValue));
        }
    }

    public sealed class Handler(IAdminQueries adminQueries)
        : IRequestHandler<Query, BusinessResult<GetAllInvoicesResponse>>
    {
        public async Task<BusinessResult<GetAllInvoicesResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var filter = new AdminInvoiceFilter(
                query.Type, query.CountryCode, query.Recipient, query.DateFrom, query.DateTo);

            var page = await adminQueries.GetAllInvoicesPagedAsync(
                filter, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetAllInvoicesResponse(page));
        }
    }
}
