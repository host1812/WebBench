using AuthorsBooks.Domain.Books;
using AuthorsBooks.Domain.Common;

namespace AuthorsBooks.Domain.Authors;

public sealed class Author
{
    private readonly List<Book> _books = [];

    private Author()
    {
    }

    private Author(Guid id, string name, string? bio, DateTimeOffset utcNow)
    {
        Id = id;
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Bio = Guard.NormalizeOptional(bio);
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Bio { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<Book> Books => _books.AsReadOnly();

    public static Author Create(string name, string? bio, DateTimeOffset utcNow, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, bio, utcNow);

    public void Update(string name, string? bio, DateTimeOffset utcNow)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Bio = Guard.NormalizeOptional(bio);
        UpdatedAtUtc = utcNow;
    }

    public Book AddBook(string title, int? publicationYear, string? isbn, DateTimeOffset utcNow, Guid? bookId = null)
    {
        var book = new Book(bookId ?? Guid.NewGuid(), Id, title, publicationYear, isbn, utcNow);
        _books.Add(book);
        UpdatedAtUtc = utcNow;
        return book;
    }

    public Book GetBook(Guid bookId)
    {
        var book = _books.SingleOrDefault(candidate => candidate.Id == bookId);
        return book ?? throw new DomainException($"Book '{bookId}' was not found for author '{Id}'.");
    }

    public void UpdateBook(Guid bookId, string title, int? publicationYear, string? isbn, DateTimeOffset utcNow)
    {
        var book = GetBook(bookId);
        book.Update(title, publicationYear, isbn, utcNow);
        UpdatedAtUtc = utcNow;
    }

    public void RemoveBook(Guid bookId, DateTimeOffset utcNow)
    {
        var book = GetBook(bookId);
        _books.Remove(book);
        UpdatedAtUtc = utcNow;
    }
}
