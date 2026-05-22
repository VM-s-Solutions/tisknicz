using MediatR;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Abstractions;

/// <summary>
/// Handler for a query. Per ADR 0002.
/// </summary>
public interface IQueryHandler<TQuery, TResponse>
    : IRequestHandler<TQuery, BusinessResult<TResponse>>
    where TQuery : IQuery<TResponse>
{
}
