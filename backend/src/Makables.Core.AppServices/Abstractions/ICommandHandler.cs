using MediatR;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Abstractions;

/// <summary>
/// Handler for a non-typed command. Per ADR 0002.
/// </summary>
public interface ICommandHandler<TCommand>
    : IRequestHandler<TCommand, BusinessResult>
    where TCommand : ICommand
{
}

/// <summary>
/// Handler for a command that returns a value on success.
/// </summary>
public interface ICommandHandler<TCommand, TResponse>
    : IRequestHandler<TCommand, BusinessResult<TResponse>>
    where TCommand : ICommand<TResponse>
{
}
