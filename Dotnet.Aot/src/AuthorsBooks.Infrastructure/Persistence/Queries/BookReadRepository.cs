using System.Diagnostics;
using System.Data;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthorsBooks.Infrastructure.Persistence.Queries;

internal sealed class BookReadRepository(ApplicationDbContext dbContext) : IBookReadRepository
{
    private const string GetBookByIdSql = """
        SELECT
            b.id,
            a.id,
            a.name,
            b.title,
            b.published_year,
            b.isbn,
            b.created_at,
            b.updated_at
        FROM public.books AS b
        INNER JOIN public.authors AS a ON a.id = b.author_id
        WHERE b.id = @bookId;
        """;

    private const string ListBooksSql = """
        SELECT
            b.id,
            a.id,
            a.name,
            b.title,
            b.published_year,
            b.isbn,
            b.created_at,
            b.updated_at
        FROM public.books AS b
        INNER JOIN public.authors AS a ON a.id = b.author_id
        ORDER BY b.title
        LIMIT @take;
        """;

    private const string ListBooksByAuthorSql = """
        SELECT
            b.id,
            a.id,
            a.name,
            b.title,
            b.published_year,
            b.isbn,
            b.created_at,
            b.updated_at
        FROM public.books AS b
        INNER JOIN public.authors AS a ON a.id = b.author_id
        WHERE a.id = @authorId
        ORDER BY b.title
        LIMIT @take;
        """;

    public async Task<BookResponse?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.book.get_by_id", ActivityKind.Internal);
        activity?.SetTag("book.id", bookId);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(GetBookByIdSql, connection);
            command.Parameters.AddWithValue("bookId", bookId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadBookResponse(reader);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<BookResponse>> ListAsync(int take, Guid? authorId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.book.list", ActivityKind.Internal);
        activity?.SetTag("book.take", take);
        activity?.SetTag("author.id", authorId);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(authorId.HasValue ? ListBooksByAuthorSql : ListBooksSql, connection);
            command.Parameters.AddWithValue("take", take);

            if (authorId.HasValue)
            {
                command.Parameters.AddWithValue("authorId", authorId.Value);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var books = new List<BookResponse>();

            while (await reader.ReadAsync(cancellationToken))
            {
                books.Add(ReadBookResponse(reader));
            }

            return books;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static BookResponse ReadBookResponse(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7));
}
