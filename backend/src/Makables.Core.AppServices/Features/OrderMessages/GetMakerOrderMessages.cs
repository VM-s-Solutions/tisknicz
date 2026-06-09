using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.OrderMessages;
using MediatR;

namespace Makables.Core.AppServices.Features.OrderMessages;

/// <summary>
/// Symmetric maker-host paged read (T-0079 US-maker-0011). Maker-scoped
/// projection — cross-tenant probes return an empty page.
/// </summary>
public static class GetMakerOrderMessages
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 50;

    public sealed record Query(string OrderId, int Page, int PageSize)
        : IQuery<GetMakerOrderMessagesResponse>;

    public sealed record GetMakerOrderMessagesResponse(PagedData<OrderMessageDto> Messages);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(q => q.Page)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(1, int.MaxValue / MaxPageSize)
                .WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(q => q.PageSize)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(1, MaxPageSize)
                .WithErrorCode(BusinessErrorMessage.MinValue);
        }
    }

    public sealed class Handler(
        IOrderMessageQueries messageQueries,
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetMakerOrderMessagesResponse>>
    {
        public async Task<BusinessResult<GetMakerOrderMessagesResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<GetMakerOrderMessagesResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                // Maker-audience JWT without a maker row → empty page (no leak).
                return BusinessResult.Success(new GetMakerOrderMessagesResponse(
                    PagedData<OrderMessageDto>.Empty(query.Page, query.PageSize)));
            }

            var page = await messageQueries.GetByOrderForMakerAsync(
                query.OrderId, maker.Id, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetMakerOrderMessagesResponse(page));
        }
    }
}
