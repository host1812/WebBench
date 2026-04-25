using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Abstractions.Telemetry;
using AuthorsBooks.Infrastructure.Persistence;
using AuthorsBooks.Infrastructure.Persistence.CompiledModels;
using AuthorsBooks.Infrastructure.Persistence.Queries;
using AuthorsBooks.Infrastructure.Persistence.Repositories;
using AuthorsBooks.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AuthorsBooks.Infrastructure;

public static class ServiceCollectionExtensions
{
    private const int DefaultDatabaseMaxConnections = 10;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresConnectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' was not found.");
        var maxConnections = GetDatabaseMaxConnections(configuration);
        var postgresConnectionStringBuilder = new NpgsqlConnectionStringBuilder(postgresConnectionString)
        {
            MaxPoolSize = maxConnections,
            SearchPath = "public",
        };
        var telemetrySettings = GetTelemetrySettings(configuration);

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IRequestTelemetry, RequestTelemetry>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(
                postgresConnectionStringBuilder.ConnectionString,
                postgres =>
                {
                    postgres.EnableRetryOnFailure();
                    postgres.CommandTimeout(600);
                    postgres.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                });
            options.UseModel(ApplicationDbContextModel.Instance);

            if (serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddScoped<IAuthorRepository, AuthorRepository>();
        services.AddScoped<IAuthorReadRepository, AuthorReadRepository>();
        services.AddScoped<IBookReadRepository, BookReadRepository>();
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<ApplicationDbContext>());

        services.AddOpenTelemetry();

        services.ConfigureOpenTelemetryTracerProvider((_, tracerProviderBuilder) =>
        {
            tracerProviderBuilder.AddSource(ServiceTelemetry.ActivitySourceName);

            if (telemetrySettings.Enabled)
            {
                tracerProviderBuilder.AddOtlpExporter(options =>
                {
                    options.Endpoint = telemetrySettings.Endpoint!;
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
            }
        });

        services.ConfigureOpenTelemetryMeterProvider((_, meterProviderBuilder) =>
        {
            meterProviderBuilder.AddMeter(ServiceTelemetry.MeterName);
        });

        return services;
    }

    private static int GetDatabaseMaxConnections(IConfiguration configuration)
    {
        var maxConnections = configuration.GetValue<int?>("Database:MaxConnections") ?? DefaultDatabaseMaxConnections;

        if (maxConnections < 1)
        {
            throw new InvalidOperationException("Configuration value 'Database:MaxConnections' must be 1 or greater.");
        }

        return maxConnections;
    }

    private static TelemetrySettings GetTelemetrySettings(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool?>("Telemetry:Enabled") ?? false;

        if (!enabled)
        {
            return new TelemetrySettings(false, null);
        }

        var endpoint = configuration["Telemetry:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Configuration value 'Telemetry:OtlpEndpoint' is required when telemetry is enabled.");
        }

        return new TelemetrySettings(true, NormalizeOtlpEndpoint(endpoint));
    }

    private static Uri NormalizeOtlpEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = $"http://{trimmed}";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Configuration value 'Telemetry:OtlpEndpoint' must be a valid absolute URI or host:port pair. Value: '{endpoint}'.");
        }

        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            uri = new UriBuilder(uri)
            {
                Path = "/v1/traces",
            }.Uri;
        }

        return uri;
    }

    private sealed record TelemetrySettings(bool Enabled, Uri? Endpoint);
}
