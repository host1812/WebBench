using AuthorsBooks.Application.Books.Queries;

namespace AuthorsBooks.Application.Abstractions.Persistence;

public interface IBookReadRepository
{
    Task<BookResponse?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BookResponse>> ListAsync(int take, Guid? authorId, CancellationToken cancellationToken);
}
