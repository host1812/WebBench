using AuthorsBooks.Api.Contracts;
using Npgsql;

namespace AuthorsBooks.Api.Database;

public sealed class NpgsqlBooksDb(
    NpgsqlDataSource dataSource,
    ILogger<NpgsqlBooksDb> logger)
    : IBooksDb
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Database ping failed.");
            return false;
        }
    }

    public async Task<AuthorResponse> CreateAuthorAsync(string name, string bio, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var author = new AuthorResponse(Guid.CreateVersion7(), name, bio, now, now);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO authors (id, name, bio, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5);
            """,
            connection);

        command.Parameters.AddWithValue(author.Id);
        command.Parameters.AddWithValue(author.Name);
        command.Parameters.AddWithValue(author.Bio);
        command.Parameters.AddWithValue(author.CreatedAt);
        command.Parameters.AddWithValue(author.UpdatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return author;
    }

    public async Task<AuthorResponse[]> ListAuthorsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, name, bio, created_at, updated_at
            FROM authors
            ORDER BY name ASC, created_at ASC;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var authors = new List<AuthorResponse>();

        while (await reader.ReadAsync(cancellationToken))
        {
            authors.Add(ReadAuthor(reader));
        }

        return [.. authors];
    }

    public async Task<AuthorResponse?> GetAuthorAsync(Guid authorId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, name, bio, created_at, updated_at
            FROM authors
            WHERE id = $1;
            """,
            connection);

        command.Parameters.AddWithValue(authorId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAuthor(reader) : null;
    }

    public async Task<AuthorResponse?> UpdateAuthorAsync(Guid authorId, string name, string bio, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE authors
            SET name = $2, bio = $3, updated_at = $4
            WHERE id = $1
            RETURNING id, name, bio, created_at, updated_at;
            """,
            connection);

        command.Parameters.AddWithValue(authorId);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(bio);
        command.Parameters.AddWithValue(now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAuthor(reader) : null;
    }

    public async Task<bool> DeleteAuthorAsync(Guid authorId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM authors WHERE id = $1;", connection);
        command.Parameters.AddWithValue(authorId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> AuthorExistsAsync(Guid authorId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1 FROM authors WHERE id = $1;", connection);
        command.Parameters.AddWithValue(authorId);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<BookResponse?> CreateBookAsync(BookWriteInput input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var book = new BookResponse(Guid.CreateVersion7(), input.AuthorId, input.Title, input.Isbn, input.PublishedYear, now, now);

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO books (id, author_id, title, isbn, published_year, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7);
                """,
                connection);

            command.Parameters.AddWithValue(book.Id);
            command.Parameters.AddWithValue(book.AuthorId);
            command.Parameters.AddWithValue(book.Title);
            command.Parameters.AddWithValue(book.Isbn);
            command.Parameters.AddWithValue((object?)book.PublishedYear ?? DBNull.Value);
            command.Parameters.AddWithValue(book.CreatedAt);
            command.Parameters.AddWithValue(book.UpdatedAt);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return book;
        }
        catch (PostgresException exception) when (PostgresErrors.IsForeignKeyViolation(exception))
        {
            return null;
        }
    }

    public Task<BookResponse[]> ListBooksAsync(int limit, CancellationToken cancellationToken) =>
        ListBooksAsync(
            """
            SELECT id, author_id, title, isbn, published_year, created_at, updated_at
            FROM books
            ORDER BY title ASC
            LIMIT $1;
            """,
            static (command, requestLimit, _) => command.Parameters.AddWithValue(requestLimit),
            limit,
            cancellationToken);

    public Task<BookResponse[]> ListBooksByAuthorAsync(Guid authorId, int limit, CancellationToken cancellationToken) =>
        ListBooksAsync(
            """
            SELECT id, author_id, title, isbn, published_year, created_at, updated_at
            FROM books
            WHERE author_id = $1
            ORDER BY title ASC
            LIMIT $2;
            """,
            static (command, requestLimit, requestAuthorId) =>
            {
                command.Parameters.AddWithValue(requestAuthorId);
                command.Parameters.AddWithValue(requestLimit);
            },
            limit,
            cancellationToken,
            authorId);

    public async Task<BookResponse?> GetBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, author_id, title, isbn, published_year, created_at, updated_at
            FROM books
            WHERE id = $1;
            """,
            connection);

        command.Parameters.AddWithValue(bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBook(reader) : null;
    }

    public async Task<BookResponse?> UpdateBookAsync(Guid bookId, BookWriteInput input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                UPDATE books
                SET author_id = $2, title = $3, isbn = $4, published_year = $5, updated_at = $6
                WHERE id = $1
                RETURNING id, author_id, title, isbn, published_year, created_at, updated_at;
                """,
                connection);

            command.Parameters.AddWithValue(bookId);
            command.Parameters.AddWithValue(input.AuthorId);
            command.Parameters.AddWithValue(input.Title);
            command.Parameters.AddWithValue(input.Isbn);
            command.Parameters.AddWithValue((object?)input.PublishedYear ?? DBNull.Value);
            command.Parameters.AddWithValue(now);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadBook(reader) : null;
        }
        catch (PostgresException exception) when (PostgresErrors.IsForeignKeyViolation(exception))
        {
            return null;
        }
    }

    public async Task<bool> DeleteBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM books WHERE id = $1;", connection);
        command.Parameters.AddWithValue(bookId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<BookResponse[]> ListBooksAsync(
        string sql,
        Action<NpgsqlCommand, int, Guid> addParameters,
        int limit,
        CancellationToken cancellationToken,
        Guid authorId = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        addParameters(command, limit, authorId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var books = new List<BookResponse>(Math.Min(limit, 256));

        while (await reader.ReadAsync(cancellationToken))
        {
            books.Add(ReadBook(reader));
        }

        return [.. books];
    }

    private static AuthorResponse ReadAuthor(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4));

    private static BookResponse ReadBook(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6));
}
