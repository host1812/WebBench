using AuthorsBooks.Domain.Books;

namespace AuthorsBooks.Application.Books.Queries;

public sealed record BookResponse(
    Guid Id,
    Guid AuthorId,
    string AuthorName,
    string Title,
    int? PublicationYear,
    string Isbn,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public static class BookMappings
{
    public static BookResponse ToResponse(this Book book, string authorName) =>
        new(
            book.Id,
            book.AuthorId,
            authorName,
            book.Title,
            book.PublicationYear,
            book.Isbn,
            book.CreatedAtUtc,
            book.UpdatedAtUtc);
}
