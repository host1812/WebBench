using System.Reflection;
using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace AuthorsBooks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddScoped<IRequestDispatcher, RequestDispatcher>();
        services.AddScoped(typeof(RequestExecutor<,>));
        services.AddScoped(typeof(IRequestBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IRequestBehavior<,>), typeof(TelemetryBehavior<,>));

        RegisterClosedGenerics(services, assembly, typeof(IRequestHandler<,>));
        RegisterClosedGenerics(services, assembly, typeof(IValidator<>));

        return services;
    }

    private static void RegisterClosedGenerics(IServiceCollection services, Assembly assembly, Type contractDefinition)
    {
        var implementations = assembly
            .DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false });

        foreach (var implementation in implementations)
        {
            var matchingContracts = implementation.ImplementedInterfaces
                .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == contractDefinition);

            foreach (var contract in matchingContracts)
            {
                services.AddScoped(contract, implementation.AsType());
            }
        }
    }
}
