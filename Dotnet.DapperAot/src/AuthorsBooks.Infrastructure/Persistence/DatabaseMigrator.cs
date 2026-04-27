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

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(@lockId);", connection))
        {
            lockCommand.Parameters.AddWithValue("lockId", MigrationLockId);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await EnsureMigrationTableAsync(connection, cancellationToken);
            var applied = new HashSet<string>(StringComparer.Ordinal);
            await using (var selectAppliedCommand = new NpgsqlCommand(
                "SELECT migration_id FROM public.__schema_migrations ORDER BY migration_id;",
                connection))
            await using (var reader = await selectAppliedCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            foreach (var migration in Migrations.Where(candidate => !applied.Contains(candidate.Id)))
            {
                logger.LogInformation("Applying database migration {MigrationId}.", migration.Id);

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

                try
                {
                    var sql = SqlScriptLoader.ReadEmbedded(migration.ResourceName);
                    await using (var migrationCommand = new NpgsqlCommand(sql, connection, transaction))
                    {
                        migrationCommand.CommandTimeout = CommandTimeoutSeconds;
                        await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (var insertCommand = new NpgsqlCommand(
                        """
                        INSERT INTO public.__schema_migrations (migration_id, applied_at_utc)
                        VALUES (@migrationId, NOW());
                        """,
                        connection,
                        transaction))
                    {
                        insertCommand.Parameters.AddWithValue("migrationId", migration.Id);
                        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

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
            await using var unlockCommand = new NpgsqlCommand("SELECT pg_advisory_unlock(@lockId);", connection);
            unlockCommand.Parameters.AddWithValue("lockId", MigrationLockId);
            await unlockCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureMigrationTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS public.__schema_migrations (
                migration_id text PRIMARY KEY,
                applied_at_utc timestamp with time zone NOT NULL
            );
            """,
            connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record MigrationDefinition(string Id, string ResourceName);
}
