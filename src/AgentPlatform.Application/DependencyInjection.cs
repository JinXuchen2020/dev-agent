using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Behaviors;
using AgentPlatform.Application.EventHandlers;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Application;

/// <summary>
/// Provides extension methods for registering Application layer services into the dependency injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds Application layer services to the specified service collection, including MediatR
    /// and the unit-of-work pipeline behavior.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The same service collection instance so calls can be chained.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
        });

        return services;
    }
}
