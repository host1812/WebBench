using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Infrastructure.Telemetry;
using Dapper;
using Npgsql;

namespace AuthorsBooks.Infrastructure.Persistence.Queries;

internal sealed class BookReadRepository(NpgsqlDataSource dataSource) : IBookReadRepository
{
    public async Task<BookResponse?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.book.get_by_id", ActivityKind.Internal);
        activity?.SetTag("book.id", bookId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<BookRow>(
            new CommandDefinition(
                """
                SELECT
                    b.id AS Id,
                    b.author_id AS AuthorId,
                    a.name AS AuthorName,
                    b.title AS Title,
                    b.published_year AS PublicationYear,
                    b.isbn AS Isbn,
                    b.created_at AS CreatedAtUtc,
                    b.updated_at AS UpdatedAtUtc
                FROM books b
                JOIN authors a ON a.id = b.author_id
                WHERE b.id = @BookId;
                """,
                new { BookId = bookId },
                cancellationToken: cancellationToken));

        return row?.ToResponse();
    }

    public async Task<IReadOnlyList<BookResponse>> ListAsync(int take, Guid? authorId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.book.list", ActivityKind.Internal);
        activity?.SetTag("book.take", take);
        activity?.SetTag("author.id", authorId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var books = await connection.QueryAsync<BookRow>(
            new CommandDefinition(
                """
                SELECT
                    b.id AS Id,
                    b.author_id AS AuthorId,
                    a.name AS AuthorName,
                    b.title AS Title,
                    b.published_year AS PublicationYear,
                    b.isbn AS Isbn,
                    b.created_at AS CreatedAtUtc,
                    b.updated_at AS UpdatedAtUtc
                FROM books b
                JOIN authors a ON a.id = b.author_id
                WHERE @AuthorId IS NULL OR b.author_id = @AuthorId
                ORDER BY b.title
                LIMIT @Take;
                """,
                new { AuthorId = authorId, Take = take },
                cancellationToken: cancellationToken));

        return books.Select(book => book.ToResponse()).ToArray();
    }

    private sealed class BookRow
    {
        public Guid Id { get; init; }

        public Guid AuthorId { get; init; }

        public string AuthorName { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public int? PublicationYear { get; init; }

        public string Isbn { get; init; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; }

        public BookResponse ToResponse() =>
            new(
                Id,
                AuthorId,
                AuthorName,
                Title,
                PublicationYear,
                Isbn,
                CreatedAtUtc,
                UpdatedAtUtc);
    }
}
