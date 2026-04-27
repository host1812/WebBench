using Microsoft.Extensions.Configuration;

namespace AuthorsBooks.Api.Database;

public sealed record AppConfig(
    string HttpAddress,
    string DatabaseConnectionString,
    int MaxConnections,
    string ServiceName)
{
    private const int DefaultMaxConnections = 10;

    public static AppConfig FromConfiguration(IConfiguration configuration, bool preferMigrationConnectionString)
    {
        var connectionString = preferMigrationConnectionString
            ? FirstNonEmpty(
                configuration["MIGRATIONS_DATABASE_CONNECTION_STRING"],
                configuration["BOOKSVC_DATABASE_CONNECTION_STRING"],
                configuration.GetConnectionString("Postgres"),
                configuration["LOCAL_DATABASE_CONNECTION_STRING"])
            : FirstNonEmpty(
                configuration["BOOKSVC_DATABASE_CONNECTION_STRING"],
                configuration.GetConnectionString("Postgres"),
                configuration["LOCAL_DATABASE_CONNECTION_STRING"]);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A PostgreSQL connection string is required.");
        }

        var maxConnections = configuration.GetValue<int?>("BOOKSVC_DATABASE_MAX_CONNECTIONS")
            ?? configuration.GetValue<int?>("Database:MaxConnections")
            ?? DefaultMaxConnections;

        if (maxConnections < 1)
        {
            throw new InvalidOperationException("Database max connections must be 1 or greater.");
        }

        return new AppConfig(
            NormalizeHttpAddress(configuration["BOOKSVC_HTTP_ADDRESS"]),
            connectionString,
            maxConnections,
            FirstNonEmpty(configuration["BOOKSVC_SERVICE_NAME"], "books-service"));
    }

    private static string NormalizeHttpAddress(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "http://0.0.0.0:8080";
        }

        return trimmed.StartsWith(":", StringComparison.Ordinal)
            ? $"http://0.0.0.0{trimmed}"
            : trimmed;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
