using Npgsql;

namespace AuthorsBooks.Api.Database;

public static class PostgresErrors
{
    private const string ForeignKeyViolationSqlState = "23503";

    public static bool IsForeignKeyViolation(PostgresException exception) =>
        string.Equals(exception.SqlState, ForeignKeyViolationSqlState, StringComparison.Ordinal);
}
