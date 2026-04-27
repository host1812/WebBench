using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Domain.Authors;
using AuthorsBooks.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace AuthorsBooks.Infrastructure.Persistence.Repositories;

internal sealed class AuthorRepository(
    AuthorPersistenceSession session,
    ILogger<AuthorRepository> logger)
    : IAuthorRepository
{
    public async Task AddAsync(Author author, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("repository.author.add", ActivityKind.Internal);
        activity?.SetTag("author.id", author.Id);
        logger.LogDebug("Adding author {AuthorId}.", author.Id);
        session.TrackAdded(author);
        await Task.CompletedTask;
    }

    public async Task<Author?> GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
    {
        if (session.TryGetTracked(authorId, out var trackedAuthor))
        {
            return trackedAuthor;
        }

        using var activity = ServiceTelemetry.StartActivity("repository.author.get_by_id", ActivityKind.Internal);
        activity?.SetTag("author.id", authorId);
        logger.LogDebug("Loading author {AuthorId}.", authorId);

        var author = await session.LoadAuthorAsync(authorId, cancellationToken);
        if (author is not null)
        {
            session.TrackLoaded(author);
        }

        return author;
    }

    public void Remove(Author author)
    {
        using var activity = ServiceTelemetry.StartActivity("repository.author.remove", ActivityKind.Internal);
        activity?.SetTag("author.id", author.Id);
        logger.LogDebug("Removing author {AuthorId}.", author.Id);
        session.TrackRemoved(author);
    }
}
