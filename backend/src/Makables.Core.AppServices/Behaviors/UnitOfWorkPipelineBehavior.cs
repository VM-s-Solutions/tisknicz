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
///
/// Unique-violation race translation:
///   <see cref="UniqueConstraintViolationException"/> from
///   <see cref="IUnitOfWork.SaveChangesAsync"/> is mapped via
///   <see cref="IUniqueConstraintTranslator"/> to a typed
///   <see cref="BusinessResult"/> failure. A handler's pre-check on
///   uniqueness (e.g. <c>EmailExistsAsync</c>) can lose a TOCTOU race
///   against a concurrent insert; without this catch the loser sees a
///   raw 500 instead of the same <c>Conflict</c> the pre-check would
///   have returned. Constraint names not in the translator's table
///   bubble as-is (a brand-new index nobody mapped is a bug worth
///   surfacing). T-0033 reviewer security M-1.
/// </summary>
public sealed class UnitOfWorkPipelineBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    IUniqueConstraintTranslator constraintTranslator)
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
        if (!commit)
        {
            return response;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return response;
        }
        catch (UniqueConstraintViolationException ex)
        {
            var translated = constraintTranslator.Translate(ex.ConstraintName);
            if (translated is null)
            {
                throw;
            }
            return BuildFailureResponse(translated);
        }
    }

    /// <summary>
    /// TResponse is constrained to <see cref="BusinessResult"/>; runtime
    /// it's either the non-generic base (rare) or
    /// <see cref="BusinessResult{T}"/>. The generic case calls the
    /// typed factory via reflection — same shape as
    /// <c>ValidationPipelineBehavior</c>.
    /// </summary>
    private static TResponse BuildFailureResponse(Error error)
    {
        BusinessResult failure;
        if (typeof(TResponse) == typeof(BusinessResult))
        {
            failure = BusinessResult.Failure(error);
        }
        else
        {
            var valueType = typeof(TResponse).GetGenericArguments()[0];
            var factory = typeof(BusinessResult)
                .GetMethod(nameof(BusinessResult.Failure), 1, new[] { typeof(Error) })!
                .MakeGenericMethod(valueType);
            failure = (BusinessResult)factory.Invoke(null, new object[] { error })!;
        }
        return (TResponse)failure;
    }
}
