using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Domain.Authors;
using AuthorsBooks.Domain.Books;
using AuthorsBooks.Infrastructure.Telemetry;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AuthorsBooks.Infrastructure.Persistence;

internal sealed class AuthorPersistenceSession(
    NpgsqlDataSource dataSource,
    ILogger<AuthorPersistenceSession> logger)
    : IUnitOfWork
{
    private const int CommandTimeoutSeconds = 600;
    private readonly Dictionary<Guid, TrackedAuthorState> _trackedAuthors = [];

    public bool TryGetTracked(Guid authorId, out Author? author)
    {
        if (_trackedAuthors.TryGetValue(authorId, out var state) && !state.IsDeleted)
        {
            author = state.Author;
            return true;
        }

        author = null;
        return false;
    }

    public async Task<Author?> LoadAuthorAsync(Guid authorId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("persistence.author.load", ActivityKind.Internal);
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
                    b.title AS Title,
                    b.published_year AS PublicationYear,
                    b.isbn AS Isbn,
                    b.created_at AS CreatedAtUtc,
                    b.updated_at AS UpdatedAtUtc
                FROM books b
                WHERE b.author_id = @AuthorId
                ORDER BY b.title;
                """,
                new { AuthorId = authorId },
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken));

        var authorRow = await grid.ReadSingleOrDefaultAsync<AuthorRow>();
        if (authorRow is null)
        {
            return null;
        }

        var books = (await grid.ReadAsync<BookRow>())
            .Select(book => Book.Rehydrate(
                book.Id,
                book.AuthorId,
                book.Title,
                book.PublicationYear,
                book.Isbn,
                book.CreatedAtUtc,
                book.UpdatedAtUtc))
            .ToArray();

        return Author.Rehydrate(
            authorRow.Id,
            authorRow.Name,
            authorRow.Bio,
            authorRow.CreatedAtUtc,
            authorRow.UpdatedAtUtc,
            books);
    }

    public void TrackLoaded(Author author)
    {
        _trackedAuthors.TryAdd(author.Id, new TrackedAuthorState(author, isNew: false));
    }

    public void TrackAdded(Author author)
    {
        _trackedAuthors[author.Id] = new TrackedAuthorState(author, isNew: true);
    }

    public void TrackRemoved(Author author)
    {
        if (_trackedAuthors.TryGetValue(author.Id, out var state))
        {
            if (state.IsNew)
            {
                _trackedAuthors.Remove(author.Id);
                return;
            }

            state.IsDeleted = true;
            return;
        }

        _trackedAuthors[author.Id] = new TrackedAuthorState(author, isNew: false) { IsDeleted = true };
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (_trackedAuthors.Count == 0)
        {
            return;
        }

        using var activity = ServiceTelemetry.StartActivity("persistence.author.save_changes", ActivityKind.Internal);
        activity?.SetTag("tracked.count", _trackedAuthors.Count);
        logger.LogDebug("Saving {TrackedCount} tracked author aggregates.", _trackedAuthors.Count);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var state in _trackedAuthors.Values.Where(candidate => candidate.IsDeleted))
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM authors WHERE id = @Id;",
                        new { state.Author.Id },
                        transaction,
                        CommandTimeoutSeconds,
                        cancellationToken: cancellationToken));
            }

            foreach (var state in _trackedAuthors.Values.Where(candidate => !candidate.IsDeleted))
            {
                if (state.IsNew)
                {
                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            """
                            INSERT INTO authors (id, name, bio, created_at, updated_at)
                            VALUES (@Id, @Name, @Bio, @CreatedAtUtc, @UpdatedAtUtc);
                            """,
                            new
                            {
                                state.Author.Id,
                                state.Author.Name,
                                state.Author.Bio,
                                state.Author.CreatedAtUtc,
                                state.Author.UpdatedAtUtc,
                            },
                            transaction,
                            CommandTimeoutSeconds,
                            cancellationToken: cancellationToken));
                }
                else
                {
                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            """
                            UPDATE authors
                            SET name = @Name,
                                bio = @Bio,
                                updated_at = @UpdatedAtUtc
                            WHERE id = @Id;
                            """,
                            new
                            {
                                state.Author.Id,
                                state.Author.Name,
                                state.Author.Bio,
                                state.Author.UpdatedAtUtc,
                            },
                            transaction,
                            CommandTimeoutSeconds,
                            cancellationToken: cancellationToken));

                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            "DELETE FROM books WHERE author_id = @AuthorId;",
                            new { AuthorId = state.Author.Id },
                            transaction,
                            CommandTimeoutSeconds,
                            cancellationToken: cancellationToken));
                }

                await InsertBooksAsync(connection, transaction, state.Author, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        foreach (var deletedAuthorId in _trackedAuthors.Where(pair => pair.Value.IsDeleted).Select(pair => pair.Key).ToArray())
        {
            _trackedAuthors.Remove(deletedAuthorId);
        }

        foreach (var state in _trackedAuthors.Values)
        {
            state.IsNew = false;
        }
    }

    private static async Task InsertBooksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Author author,
        CancellationToken cancellationToken)
    {
        if (author.Books.Count == 0)
        {
            return;
        }

        var batch = new NpgsqlBatch(connection, transaction);

        foreach (var book in author.Books)
        {
            var command = new NpgsqlBatchCommand(
                """
                INSERT INTO books (id, author_id, title, published_year, isbn, created_at, updated_at)
                VALUES (@Id, @AuthorId, @Title, @PublicationYear, @Isbn, @CreatedAtUtc, @UpdatedAtUtc);
                """);

            command.Parameters.AddWithValue("Id", book.Id);
            command.Parameters.AddWithValue("AuthorId", book.AuthorId);
            command.Parameters.AddWithValue("Title", book.Title);
            command.Parameters.AddWithValue("PublicationYear", book.PublicationYear ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("Isbn", book.Isbn);
            command.Parameters.AddWithValue("CreatedAtUtc", book.CreatedAtUtc);
            command.Parameters.AddWithValue("UpdatedAtUtc", book.UpdatedAtUtc);

            batch.BatchCommands.Add(command);
        }

        batch.Timeout = CommandTimeoutSeconds;
        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record AuthorRow(
        Guid Id,
        string Name,
        string Bio,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record BookRow(
        Guid Id,
        Guid AuthorId,
        string Title,
        int? PublicationYear,
        string Isbn,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed class TrackedAuthorState(Author author, bool isNew)
    {
        public Author Author { get; } = author;

        public bool IsDeleted { get; set; }

        public bool IsNew { get; set; } = isNew;
    }
}
