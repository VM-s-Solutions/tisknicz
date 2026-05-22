using MediatR;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Abstractions;

/// <summary>
/// Marker for a CQRS command that mutates state and returns a non-typed
/// <see cref="BusinessResult"/>. Per ADR 0002 and patterns §A.3.
/// </summary>
public interface ICommand : IRequest<BusinessResult>
{
}

/// <summary>
/// Marker for a CQRS command that mutates state and returns a value
/// on success wrapped in <see cref="BusinessResult{T}"/>.
/// </summary>
public interface ICommand<TResponse> : IRequest<BusinessResult<TResponse>>
{
}
