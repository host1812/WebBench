using AuthorsBooks.Api.Contracts;

namespace AuthorsBooks.Api.Database;

public interface IBooksDb
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);

    Task<AuthorResponse> CreateAuthorAsync(string name, string bio, DateTimeOffset now, CancellationToken cancellationToken);

    Task<AuthorResponse[]> ListAuthorsAsync(CancellationToken cancellationToken);

    Task<AuthorResponse?> GetAuthorAsync(Guid authorId, CancellationToken cancellationToken);

    Task<AuthorResponse?> UpdateAuthorAsync(Guid authorId, string name, string bio, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> DeleteAuthorAsync(Guid authorId, CancellationToken cancellationToken);

    Task<bool> AuthorExistsAsync(Guid authorId, CancellationToken cancellationToken);

    Task<BookResponse?> CreateBookAsync(BookWriteInput input, DateTimeOffset now, CancellationToken cancellationToken);

    Task<BookResponse[]> ListBooksAsync(int limit, CancellationToken cancellationToken);

    Task<BookResponse[]> ListBooksByAuthorAsync(Guid authorId, int limit, CancellationToken cancellationToken);

    Task<BookResponse?> GetBookAsync(Guid bookId, CancellationToken cancellationToken);

    Task<BookResponse?> UpdateBookAsync(Guid bookId, BookWriteInput input, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> DeleteBookAsync(Guid bookId, CancellationToken cancellationToken);
}
