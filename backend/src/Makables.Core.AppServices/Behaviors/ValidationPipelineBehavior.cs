using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs every <see cref="IValidator{TRequest}"/>
/// registered for the request, BEFORE the handler. On failure it returns a
/// <see cref="BusinessResult"/> validation failure WITHOUT calling the
/// handler. Per ADR 0002 / patterns §A.5.
///
/// Maps FluentValidation's <see cref="ValidationFailure"/> to our own
/// <see cref="ValidationDetail"/> so <see cref="BusinessResult"/> in
/// <c>Core.Domain</c> stays free of the FluentValidation dependency.
///
/// Only operates on responses that derive from <see cref="BusinessResult"/>
/// (the convention for every command / query in this codebase per the
/// <c>ICommand</c> / <c>IQuery</c> marker interfaces).
/// </summary>
public sealed class ValidationPipelineBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : BusinessResult
{
    private readonly IReadOnlyList<IValidator<TRequest>> _validators = validators.ToList();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Count == 0)
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next(cancellationToken);
        }

        var details = failures
            .Select(f => new ValidationDetail(
                f.PropertyName,
                f.ErrorCode ?? f.ErrorMessage,
                f.ErrorMessage))
            .ToList();

        // TResponse is constrained to BusinessResult; runtime it's either
        // BusinessResult (non-generic command) or BusinessResult<TValue>
        // (typed command/query). The non-generic case is a direct cast; the
        // generic case calls the typed factory via reflection.
        BusinessResult failure;
        if (typeof(TResponse) == typeof(BusinessResult))
        {
            failure = BusinessResult.ValidationFailure(details);
        }
        else
        {
            var valueType = typeof(TResponse).GetGenericArguments()[0];
            var factory = typeof(BusinessResult)
                .GetMethod(
                    nameof(BusinessResult.ValidationFailure),
                    1,
                    new[] { typeof(IReadOnlyList<ValidationDetail>) })!
                .MakeGenericMethod(valueType);
            failure = (BusinessResult)factory.Invoke(null, new object[] { details })!;
        }

        return (TResponse)failure;
    }
}
