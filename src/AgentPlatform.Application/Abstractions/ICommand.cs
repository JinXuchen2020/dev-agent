using MediatR;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Marks a command that produces a result of type <typeparamref name="TResponse"/>,
/// integrating with the MediatR request pipeline.
/// </summary>
/// <typeparam name="TResponse">The type of the result produced by the command.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>
{
}

/// <summary>
/// Marks a command that does not produce a result, integrating with the MediatR request pipeline.
/// </summary>
public interface ICommand : IRequest
{
}
