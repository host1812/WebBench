using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Infrastructure.Telemetry;
using Dapper;
using Npgsql;

namespace AuthorsBooks.Infrastructure.Persistence.Queries;

internal sealed class AuthorReadRepository(NpgsqlDataSource dataSource) : IAuthorReadRepository
{
    public async Task<AuthorDetailsResponse?> GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.author.get_by_id", ActivityKind.Internal);
        activity?.SetTag("author.id", authorId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(
                """
                SELECT
                    a.id AS Id,
                    a.name AS Name,
                    a.bio AS Bio,
                    a.created_at AS CreatedAtUtc,
                    a.updated_at AS UpdatedAtUtc
                FROM authors a
                WHERE a.id = @AuthorId;

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
                WHERE b.author_id = @AuthorId
                ORDER BY b.title;
                """,
                new { AuthorId = authorId },
                cancellationToken: cancellationToken));

        var author = await grid.ReadSingleOrDefaultAsync<AuthorDetailsRow>();
        if (author is null)
        {
            return null;
        }

        var books = (await grid.ReadAsync<BookRow>())
            .Select(book => book.ToResponse())
            .ToArray();
        return new AuthorDetailsResponse(
            author.Id,
            author.Name,
            author.Bio,
            author.CreatedAtUtc,
            author.UpdatedAtUtc,
            books);
    }

    public async Task<IReadOnlyList<AuthorSummaryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.author.list", ActivityKind.Internal);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var authors = await connection.QueryAsync<AuthorSummaryRow>(
            new CommandDefinition(
                """
                SELECT
                    a.id AS Id,
                    a.name AS Name,
                    a.bio AS Bio,
                    a.created_at AS CreatedAtUtc,
                    a.updated_at AS UpdatedAtUtc,
                    COUNT(b.id)::int AS BookCount
                FROM authors a
                LEFT JOIN books b ON b.author_id = a.id
                GROUP BY a.id, a.name, a.bio, a.created_at, a.updated_at
                ORDER BY a.name;
                """,
                cancellationToken: cancellationToken));

        return authors.Select(author => author.ToResponse()).ToArray();
    }

    private sealed class AuthorDetailsRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Bio { get; init; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; }
    }

    private sealed class AuthorSummaryRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Bio { get; init; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; }

        public int BookCount { get; init; }

        public AuthorSummaryResponse ToResponse() =>
            new(
                Id,
                Name,
                Bio,
                CreatedAtUtc,
                UpdatedAtUtc,
                BookCount);
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
