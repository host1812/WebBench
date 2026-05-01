using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Stores.Queries;
using AuthorsBooks.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace AuthorsBooks.Infrastructure.Persistence.Queries;

internal sealed class StoreReadRepository(ApplicationDbContext dbContext) : IStoreReadRepository
{
    public async Task<IReadOnlyList<StoreResponse>> ListAsync(CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.store.list", ActivityKind.Internal);

        var stores = await dbContext.Stores
            .AsNoTracking()
            .Include(store => store.Inventory)
            .OrderBy(store => store.Name)
            .ToListAsync(cancellationToken);

        var authorIds = stores
            .SelectMany(store => store.Inventory)
            .Select(book => book.AuthorId)
            .Distinct()
            .ToArray();

        var authorNames = await dbContext.Authors
            .AsNoTracking()
            .Where(author => authorIds.Contains(author.Id))
            .ToDictionaryAsync(author => author.Id, author => author.Name, cancellationToken);

        return stores
            .Select(store => store.ToResponse(authorNames))
            .ToList();
    }
}
