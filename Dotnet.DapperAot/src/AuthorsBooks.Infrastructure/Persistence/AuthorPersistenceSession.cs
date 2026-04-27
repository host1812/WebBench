using System.Diagnostics;
using System.Data;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Domain.Authors;
using AuthorsBooks.Domain.Books;
using AuthorsBooks.Infrastructure.Telemetry;
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
        await using var command = new NpgsqlCommand(
            """
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
            FROM authors AS a
            LEFT JOIN books AS b ON b.author_id = a.id
            WHERE a.id = @authorId
            ORDER BY b.title;
            """,
            connection);
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Parameters.AddWithValue("authorId", authorId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var books = new List<Book>();
        var author = Author.Rehydrate(
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

            books.Add(Book.Rehydrate(
                reader.GetGuid(5),
                author.Id,
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetFieldValue<DateTimeOffset>(10)));
        }
        while (await reader.ReadAsync(cancellationToken));

        return author;
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
                await using var deleteAuthorCommand = new NpgsqlCommand("DELETE FROM authors WHERE id = @id;", connection, transaction);
                deleteAuthorCommand.CommandTimeout = CommandTimeoutSeconds;
                deleteAuthorCommand.Parameters.AddWithValue("id", state.Author.Id);
                await deleteAuthorCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var state in _trackedAuthors.Values.Where(candidate => !candidate.IsDeleted))
            {
                if (state.IsNew)
                {
                    await using var insertAuthorCommand = new NpgsqlCommand(
                        """
                        INSERT INTO authors (id, name, bio, created_at, updated_at)
                        VALUES (@id, @name, @bio, @createdAtUtc, @updatedAtUtc);
                        """,
                        connection,
                        transaction);
                    insertAuthorCommand.CommandTimeout = CommandTimeoutSeconds;
                    insertAuthorCommand.Parameters.AddWithValue("id", state.Author.Id);
                    insertAuthorCommand.Parameters.AddWithValue("name", state.Author.Name);
                    insertAuthorCommand.Parameters.AddWithValue("bio", state.Author.Bio);
                    insertAuthorCommand.Parameters.AddWithValue("createdAtUtc", state.Author.CreatedAtUtc);
                    insertAuthorCommand.Parameters.AddWithValue("updatedAtUtc", state.Author.UpdatedAtUtc);
                    await insertAuthorCommand.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    await using (var updateAuthorCommand = new NpgsqlCommand(
                        """
                        UPDATE authors
                        SET name = @name,
                            bio = @bio,
                            updated_at = @updatedAtUtc
                        WHERE id = @id;
                        """,
                        connection,
                        transaction))
                    {
                        updateAuthorCommand.CommandTimeout = CommandTimeoutSeconds;
                        updateAuthorCommand.Parameters.AddWithValue("id", state.Author.Id);
                        updateAuthorCommand.Parameters.AddWithValue("name", state.Author.Name);
                        updateAuthorCommand.Parameters.AddWithValue("bio", state.Author.Bio);
                        updateAuthorCommand.Parameters.AddWithValue("updatedAtUtc", state.Author.UpdatedAtUtc);
                        await updateAuthorCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (var deleteBooksCommand = new NpgsqlCommand(
                        "DELETE FROM books WHERE author_id = @authorId;",
                        connection,
                        transaction))
                    {
                        deleteBooksCommand.CommandTimeout = CommandTimeoutSeconds;
                        deleteBooksCommand.Parameters.AddWithValue("authorId", state.Author.Id);
                        await deleteBooksCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
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

    private sealed class TrackedAuthorState(Author author, bool isNew)
    {
        public Author Author { get; } = author;

        public bool IsDeleted { get; set; }

        public bool IsNew { get; set; } = isNew;
    }
}
