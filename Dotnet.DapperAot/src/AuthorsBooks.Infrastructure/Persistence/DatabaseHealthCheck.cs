using Npgsql;

namespace AuthorsBooks.Infrastructure.Persistence;

public interface IDatabaseHealthCheck
{
    Task<DatabaseHealthResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed record DatabaseHealthResult(bool IsHealthy, string Status, string? Error)
{
    public static DatabaseHealthResult Healthy() => new(true, "healthy", null);

    public static DatabaseHealthResult Unhealthy(string error) => new(false, "unhealthy", error);
}

internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IDatabaseHealthCheck
{
    public async Task<DatabaseHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return DatabaseHealthResult.Healthy();
        }
        catch (Exception exception)
        {
            return DatabaseHealthResult.Unhealthy(exception.Message);
        }
    }
}
