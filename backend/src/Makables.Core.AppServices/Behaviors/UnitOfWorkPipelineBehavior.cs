using MediatR;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.SeedWork;

namespace Makables.Core.AppServices.Behaviors;

/// <summary>
/// MediatR pipeline behavior that calls <see cref="IUnitOfWork.SaveChangesAsync"/>
/// after a command handler returns successfully. Per ADR 0002 / patterns §A.5.
///
/// CRITICAL: handlers MUST NOT call <see cref="IUnitOfWork.SaveChangesAsync"/>
/// themselves. The Reviewer rejects PRs that do.
///
/// Wraps ONLY <see cref="ICommand"/> requests. Queries skip this behavior
/// because they don't mutate state.
/// </summary>
public sealed class UnitOfWorkPipelineBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandMarker
    where TResponse : BusinessResult
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (response.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return response;
    }
}
