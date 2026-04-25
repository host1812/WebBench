using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace AuthorsBooks.Infrastructure.Persistence;

public static class MigrationBootstrapper
{
    private const string InitialMigrationId = "20260424190000_InitialSharedSchema";
    private const string ProductVersion = "10.0.4";
    private const string SchemaName = "public";

    public static async Task BaselineExistingSharedSchemaAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken))
        {
            return;
        }

        var hasAuthors = await TableExistsAsync(connection, "authors", cancellationToken);
        var hasBooks = await TableExistsAsync(connection, "books", cancellationToken);

        if (!hasAuthors || !hasBooks)
        {
            return;
        }

        await using (var createHistory = connection.CreateCommand())
        {
            createHistory.CommandText = """
                CREATE TABLE IF NOT EXISTS public."__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );
                """;

            await createHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var seedHistory = connection.CreateCommand();
        seedHistory.CommandText = """
            INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES (@migrationId, @productVersion)
            ON CONFLICT ("MigrationId") DO NOTHING;
            """;

        AddParameter(seedHistory, "@migrationId", InitialMigrationId);
        AddParameter(seedHistory, "@productVersion", ProductVersion);

        await seedHistory.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schemaName
                  AND table_name = @tableName
            );
            """;

        AddParameter(command, "@schemaName", SchemaName);
        AddParameter(command, "@tableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true || result is bool boolResult && boolResult;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
