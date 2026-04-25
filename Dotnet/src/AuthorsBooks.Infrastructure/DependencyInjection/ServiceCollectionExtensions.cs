using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Abstractions.Telemetry;
using AuthorsBooks.Infrastructure.Persistence;
using AuthorsBooks.Infrastructure.Persistence.Queries;
using AuthorsBooks.Infrastructure.Persistence.Repositories;
using AuthorsBooks.Infrastructure.Telemetry;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AuthorsBooks.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresConnectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' was not found.");

        var applicationInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights");

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IRequestTelemetry, RequestTelemetry>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(
                postgresConnectionString,
                postgres =>
                {
                    postgres.EnableRetryOnFailure();
                    postgres.CommandTimeout(600);
                });

            options.EnableDetailedErrors();

            if (serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddScoped<IAuthorRepository, AuthorRepository>();
        services.AddScoped<IAuthorReadRepository, AuthorReadRepository>();
        services.AddScoped<IBookReadRepository, BookReadRepository>();
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<ApplicationDbContext>());

        var openTelemetryBuilder = services.AddOpenTelemetry();

        if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
        {
            openTelemetryBuilder.UseAzureMonitor(options =>
            {
                options.ConnectionString = applicationInsightsConnectionString;
            });
        }

        services.ConfigureOpenTelemetryTracerProvider((_, tracerProviderBuilder) =>
        {
            tracerProviderBuilder.AddSource(ServiceTelemetry.ActivitySourceName);
        });

        services.ConfigureOpenTelemetryMeterProvider((_, meterProviderBuilder) =>
        {
            meterProviderBuilder.AddMeter(ServiceTelemetry.MeterName);
        });

        return services;
    }
}
