using MediatR;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.SeedWork;

namespace Makables.Core.AppServices.Behaviors;

/// <summary>
/// MediatR pipeline behavior that calls <see cref="IUnitOfWork.SaveChangesAsync"/>
/// after a command handler returns. Per ADR 0002 / patterns §A.5.
///
/// CRITICAL: handlers MUST NOT call <see cref="IUnitOfWork.SaveChangesAsync"/>
/// themselves. The Reviewer rejects PRs that do.
///
/// Wraps ONLY <see cref="ICommand"/> requests. Queries skip this behavior
/// because they don't mutate state.
///
/// Commit policy:
///   - Default: commit only on <see cref="BusinessResult.IsSuccess"/>.
///     A handler that bails out with a failure is expected to leave the
///     DbContext untouched.
///   - Opt-in for failure paths: commands marked with
///     <see cref="IPersistOnFailureCommand"/> commit regardless of
///     outcome. Required by the auth use cases whose anti-abuse and
///     security state (failed-login counters, family-wide revocation,
///     logout revoke) MUST survive a failure response — reviewer T-0022
///     BLOCKER B-1.
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

        var commit = response.IsSuccess || request is IPersistOnFailureCommand;
        if (commit)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return response;
    }
}
