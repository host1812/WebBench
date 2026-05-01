using System.Reflection;
using Npgsql;

namespace AuthorsBooks.Api.Database;

public sealed class DatabaseMigrator(
    NpgsqlDataSource dataSource,
    ILogger<DatabaseMigrator> logger)
    : IDatabaseMigrator
{
    private const long MigrationLockId = 4_658_701_321_947;
    private const int CommandTimeoutSeconds = 600;
    private static readonly MigrationDefinition[] Migrations =
    [
        new("001_initial_schema", "AuthorsBooks.Api.Sql.001_initial_schema.sql"),
        new("002_seed_catalog", "AuthorsBooks.Api.Sql.002_seed_catalog.up.sql"),
        new("003_seed_more_books", "AuthorsBooks.Api.Sql.003_seed_more_books.up.sql"),
        new("004_add_stores", "AuthorsBooks.Api.Sql.004_add_stores.up.sql"),
    ];

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using var advisoryLock = new NpgsqlCommand("SELECT pg_advisory_lock($1);", connection);
        advisoryLock.Parameters.AddWithValue(MigrationLockId);
        await advisoryLock.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            await EnsureMigrationTableAsync(connection, cancellationToken);
            var applied = await LoadAppliedMigrationsAsync(connection, cancellationToken);

            foreach (var migration in Migrations)
            {
                if (applied.Contains(migration.Id))
                {
                    continue;
                }

                logger.LogInformation("Applying database migration {MigrationId}.", migration.Id);
                await ApplyMigrationAsync(connection, migration, cancellationToken);
            }
        }
        finally
        {
            await using var advisoryUnlock = new NpgsqlCommand("SELECT pg_advisory_unlock($1);", connection);
            advisoryUnlock.Parameters.AddWithValue(MigrationLockId);
            await advisoryUnlock.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureMigrationTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql =
            """
            CREATE TABLE IF NOT EXISTS public.__schema_migrations (
                migration_id text PRIMARY KEY,
                applied_at_utc timestamp with time zone NOT NULL
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> LoadAppliedMigrationsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "SELECT migration_id FROM public.__schema_migrations ORDER BY migration_id;";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var migrations = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations;
    }

    private static async Task ApplyMigrationAsync(NpgsqlConnection connection, MigrationDefinition migration, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var sql = ReadEmbeddedSql(migration.ResourceName);

            await using (var migrationCommand = new NpgsqlCommand(sql, connection, transaction))
            {
                migrationCommand.CommandTimeout = CommandTimeoutSeconds;
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var insertCommand = new NpgsqlCommand(
                             """
                             INSERT INTO public.__schema_migrations (migration_id, applied_at_utc)
                             VALUES ($1, NOW());
                             """,
                             connection,
                             transaction))
            {
                insertCommand.Parameters.AddWithValue(migration.Id);
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

    private static string ReadEmbeddedSql(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record MigrationDefinition(string Id, string ResourceName);
}
