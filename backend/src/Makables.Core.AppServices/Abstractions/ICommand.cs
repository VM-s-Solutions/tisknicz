using MediatR;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Abstractions;

/// <summary>
/// Non-generic marker for any CQRS command. Used by
/// <c>UnitOfWorkPipelineBehavior</c> to select commands (vs queries) via
/// the type-constraint <c>where TRequest : ICommandMarker</c>, regardless
/// of whether the command returns a value.
/// </summary>
public interface ICommandMarker { }

/// <summary>
/// CQRS command that mutates state and returns a non-typed
/// <see cref="BusinessResult"/>. Per ADR 0002 / patterns §A.3.
/// </summary>
public interface ICommand : ICommandMarker, IRequest<BusinessResult>
{
}

/// <summary>
/// CQRS command that mutates state and returns a value on success
/// wrapped in <see cref="BusinessResult{T}"/>.
/// </summary>
public interface ICommand<TResponse> : ICommandMarker, IRequest<BusinessResult<TResponse>>
{
}
