using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Domain.Authors;

namespace AuthorsBooks.Application.Authors.Queries;

public sealed record AuthorSummaryResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int BookCount);

public sealed record AuthorDetailsResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<BookResponse> Books);

public static class AuthorMappings
{
    public static AuthorDetailsResponse ToDetailsResponse(this Author author) =>
        new(
            author.Id,
            author.Name,
            author.CreatedAtUtc,
            author.UpdatedAtUtc,
            author.Books
                .OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase)
                .Select(book => book.ToResponse(author.Name))
                .ToArray());
}
