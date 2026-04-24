using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace AuthorsBooks.Infrastructure.Persistence.Queries;

internal sealed class AuthorReadRepository(ApplicationDbContext dbContext) : IAuthorReadRepository
{
    public async Task<AuthorDetailsResponse?> GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.author.get_by_id", ActivityKind.Internal);
        activity?.SetTag("author.id", authorId);

        var author = await dbContext.Authors
            .AsNoTracking()
            .Include(candidate => candidate.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == authorId, cancellationToken);

        return author?.ToDetailsResponse();
    }

    public async Task<IReadOnlyList<AuthorSummaryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.author.list", ActivityKind.Internal);

        return await dbContext.Authors
            .AsNoTracking()
            .OrderBy(author => author.Name)
            .Select(author => new AuthorSummaryResponse(
                author.Id,
                author.Name,
                author.CreatedAtUtc,
                author.UpdatedAtUtc,
                author.Books.Count))
            .ToListAsync(cancellationToken);
    }
}
