using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace AuthorsBooks.Infrastructure.Persistence.Queries;

internal sealed class BookReadRepository(ApplicationDbContext dbContext) : IBookReadRepository
{
    public async Task<BookResponse?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.book.get_by_id", ActivityKind.Internal);
        activity?.SetTag("book.id", bookId);

        return await dbContext.Books
            .AsNoTracking()
            .Join(
                dbContext.Authors.AsNoTracking(),
                book => book.AuthorId,
                author => author.Id,
                (book, author) => new BookResponse(
                    book.Id,
                    author.Id,
                    author.Name,
                    book.Title,
                    book.PublicationYear,
                    book.Isbn,
                    book.CreatedAtUtc,
                    book.UpdatedAtUtc))
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);
    }

    public async Task<IReadOnlyList<BookResponse>> ListAsync(int take, Guid? authorId, CancellationToken cancellationToken)
    {
        using var activity = ServiceTelemetry.StartActivity("query.book.list", ActivityKind.Internal);
        activity?.SetTag("book.take", take);
        activity?.SetTag("author.id", authorId);

        var query = dbContext.Books
            .AsNoTracking()
            .Join(
                dbContext.Authors.AsNoTracking(),
                book => book.AuthorId,
                author => author.Id,
                (book, author) => new { Book = book, Author = author });

        if (authorId.HasValue)
        {
            query = query.Where(candidate => candidate.Author.Id == authorId.Value);
        }

        return await query
            .OrderBy(candidate => candidate.Book.Title)
            .Take(take)
            .Select(candidate => new BookResponse(
                candidate.Book.Id,
                candidate.Author.Id,
                candidate.Author.Name,
                candidate.Book.Title,
                candidate.Book.PublicationYear,
                candidate.Book.Isbn,
                candidate.Book.CreatedAtUtc,
                candidate.Book.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
