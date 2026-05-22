using MediatR;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Abstractions;

/// <summary>
/// Marker for a read-only CQRS query. Queries do not mutate state and
/// therefore do not go through the unit-of-work pipeline behavior.
/// Per ADR 0002 and patterns §A.3.
/// </summary>
public interface IQuery<TResponse> : IRequest<BusinessResult<TResponse>>
{
}
