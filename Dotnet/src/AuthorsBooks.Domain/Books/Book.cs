using AuthorsBooks.Domain.Common;

namespace AuthorsBooks.Domain.Books;

public sealed class Book
{
    private Book()
    {
    }

    internal Book(Guid id, Guid authorId, string title, int publicationYear, string? isbn, DateTimeOffset utcNow)
    {
        Id = id;
        AuthorId = authorId;
        Title = Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        PublicationYear = Guard.AgainstOutOfRange(publicationYear, 1, 9999, nameof(publicationYear));
        Isbn = NormalizeIsbn(isbn);
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public Guid Id { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public int PublicationYear { get; private set; }

    public string? Isbn { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    internal void Update(string title, int publicationYear, string? isbn, DateTimeOffset utcNow)
    {
        Title = Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        PublicationYear = Guard.AgainstOutOfRange(publicationYear, 1, 9999, nameof(publicationYear));
        Isbn = NormalizeIsbn(isbn);
        UpdatedAtUtc = utcNow;
    }

    private static string? NormalizeIsbn(string? isbn) =>
        string.IsNullOrWhiteSpace(isbn) ? null : isbn.Trim();
}
