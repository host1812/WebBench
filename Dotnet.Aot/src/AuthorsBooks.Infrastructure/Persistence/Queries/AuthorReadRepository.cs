using System.Diagnostics;
using System.Data;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthorsBooks.Infrastructure.Persistence.Queries;

internal sealed class AuthorReadRepository(ApplicationDbContext dbContext) : IAuthorReadRepository
{
    private const string GetAuthorByIdSql = """
        SELECT
            a.id,
            a.name,
            a.bio,
            a.created_at,
            a.updated_at,
            b.id,
            b.title,
            b.published_year,
            b.isbn,
            b.created_at,
            b.updated_at
        FROM public.authors AS a
        LEFT JOIN public.books AS b ON b.author_id = a.id
        WHERE a.id = @authorId
        ORDER BY b.title;
        """;

    private const string ListAuthorsSql = """
        SELECT
            a.id,
            a.name,
            a.bio,
            a.created_at,
            a.updated_at,
            COUNT(b.id)::integer AS book_count
        FROM public.authors AS a
        LEFT JOIN public.books AS b ON b.author_id = a.id
        GROUP BY a.id, a.name, a.bio, a.created_at, a.updated_at
        ORDER BY a.name;
        """;

    public async Task<AuthorDetailsResponse?> GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.author.get_by_id", ActivityKind.Internal);
        activity?.SetTag("author.id", authorId);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(GetAuthorByIdSql, connection);
            command.Parameters.AddWithValue("authorId", authorId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var books = new List<BookResponse>();
            var response = new AuthorDetailsResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                books);

            do
            {
                if (reader.IsDBNull(5))
                {
                    continue;
                }

                books.Add(new BookResponse(
                    reader.GetGuid(5),
                    response.Id,
                    response.Name,
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    reader.GetFieldValue<DateTimeOffset>(10)));
            }
            while (await reader.ReadAsync(cancellationToken));

            return response;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<AuthorSummaryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.author.list", ActivityKind.Internal);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(ListAuthorsSql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var authors = new List<AuthorSummaryResponse>();

            while (await reader.ReadAsync(cancellationToken))
            {
                authors.Add(new AuthorSummaryResponse(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetInt32(5)));
            }

            return authors;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
