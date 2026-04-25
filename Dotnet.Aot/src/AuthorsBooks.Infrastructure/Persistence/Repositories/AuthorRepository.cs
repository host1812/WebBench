using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Domain.Authors;
using AuthorsBooks.Infrastructure.Persistence;
using AuthorsBooks.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AuthorsBooks.Infrastructure.Persistence.Repositories;

internal sealed class AuthorRepository(
    ApplicationDbContext dbContext,
    ILogger<AuthorRepository> logger)
    : IAuthorRepository
{
    public async Task AddAsync(Author author, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("repository.author.add", ActivityKind.Internal);
        activity?.SetTag("author.id", author.Id);
        logger.LogDebug("Adding author {AuthorId}.", author.Id);
        await dbContext.Authors.AddAsync(author, cancellationToken);
    }

    public async Task<Author?> GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("repository.author.get_by_id", ActivityKind.Internal);
        activity?.SetTag("author.id", authorId);
        logger.LogDebug("Loading author {AuthorId}.", authorId);

        var author = await dbContext.Authors.FindAsync([authorId], cancellationToken);
        if (author is null)
        {
            return null;
        }

        await dbContext.Entry(author)
            .Collection(candidate => candidate.Books)
            .LoadAsync(cancellationToken);

        return author;
    }

    public void Remove(Author author)
    {
        using var activity = ServiceTelemetry.StartActivity("repository.author.remove", ActivityKind.Internal);
        activity?.SetTag("author.id", author.Id);
        logger.LogDebug("Removing author {AuthorId}.", author.Id);
        dbContext.Authors.Remove(author);
    }
}
