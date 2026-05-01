using AuthorsBooks.Application.Stores.Queries;

namespace AuthorsBooks.Application.Abstractions.Persistence;

public interface IStoreReadRepository
{
    Task<IReadOnlyList<StoreResponse>> ListAsync(CancellationToken cancellationToken);
}
