using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AuthorsBooks.Infrastructure.Persistence;

public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

internal sealed class DatabaseMigrator(
    NpgsqlDataSource dataSource,
    ILogger<DatabaseMigrator> logger)
    : IDatabaseMigrator
{
    private const long MigrationLockId = 4_658_701_321_947;
    private const int CommandTimeoutSeconds = 600;
    private static readonly MigrationDefinition[] Migrations =
    [
        new("001_initial_schema", "AuthorsBooks.Infrastructure.Sql.001_initial_schema.sql"),
        new("002_seed_catalog", "AuthorsBooks.Infrastructure.Sql.002_seed_catalog.up.sql"),
        new("003_seed_more_books", "AuthorsBooks.Infrastructure.Sql.003_seed_more_books.up.sql"),
    ];

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition("SELECT pg_advisory_lock(@LockId);", new { LockId = MigrationLockId }, cancellationToken: cancellationToken));

        try
        {
            await EnsureMigrationTableAsync(connection, cancellationToken);
            var applied = (await connection.QueryAsync<string>(
                new CommandDefinition(
                    "SELECT migration_id FROM public.__schema_migrations ORDER BY migration_id;",
                    cancellationToken: cancellationToken)))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var migration in Migrations.Where(candidate => !applied.Contains(candidate.Id)))
            {
                logger.LogInformation("Applying database migration {MigrationId}.", migration.Id);

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

                try
                {
                    var sql = SqlScriptLoader.ReadEmbedded(migration.ResourceName);
                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            sql,
                            transaction: transaction,
                            commandTimeout: CommandTimeoutSeconds,
                            cancellationToken: cancellationToken));

                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            """
                            INSERT INTO public.__schema_migrations (migration_id, applied_at_utc)
                            VALUES (@MigrationId, NOW());
                            """,
                            new { MigrationId = migration.Id },
                            transaction,
                            cancellationToken: cancellationToken));

                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }
        finally
        {
            await connection.ExecuteAsync(
                new CommandDefinition("SELECT pg_advisory_unlock(@LockId);", new { LockId = MigrationLockId }, cancellationToken: cancellationToken));
        }
    }

    private static Task EnsureMigrationTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            new CommandDefinition(
                """
                CREATE TABLE IF NOT EXISTS public.__schema_migrations (
                    migration_id text PRIMARY KEY,
                    applied_at_utc timestamp with time zone NOT NULL
                );
                """,
                cancellationToken: cancellationToken));

    private sealed record MigrationDefinition(string Id, string ResourceName);
}
