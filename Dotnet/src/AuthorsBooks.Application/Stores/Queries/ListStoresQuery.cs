using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;

namespace AuthorsBooks.Application.Stores.Queries;

public sealed record ListStoresQuery() : IQuery<IReadOnlyList<StoreResponse>>;

internal sealed class ListStoresQueryHandler(IStoreReadRepository storeReadRepository)
    : IRequestHandler<ListStoresQuery, IReadOnlyList<StoreResponse>>
{
    public Task<IReadOnlyList<StoreResponse>> Handle(ListStoresQuery request, CancellationToken cancellationToken) =>
        storeReadRepository.ListAsync(cancellationToken);
}
