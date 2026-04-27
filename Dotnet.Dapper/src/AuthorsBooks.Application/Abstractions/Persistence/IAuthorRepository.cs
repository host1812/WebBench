using AuthorsBooks.Domain.Authors;

namespace AuthorsBooks.Application.Abstractions.Persistence;

public interface IAuthorRepository
{
    Task AddAsync(Author author, CancellationToken cancellationToken);

    Task<Author?> GetByIdAsync(Guid authorId, CancellationToken cancellationToken);

    void Remove(Author author);
}
