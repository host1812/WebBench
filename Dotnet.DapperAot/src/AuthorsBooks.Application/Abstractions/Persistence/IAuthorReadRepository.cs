using AuthorsBooks.Application.Authors.Queries;

namespace AuthorsBooks.Application.Abstractions.Persistence;

public interface IAuthorReadRepository
{
    Task<AuthorDetailsResponse?> GetByIdAsync(Guid authorId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthorSummaryResponse>> ListAsync(CancellationToken cancellationToken);
}
