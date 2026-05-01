using AuthorsBooks.Domain.Books;
using AuthorsBooks.Domain.Common;

namespace AuthorsBooks.Domain.Stores;

public sealed class Store
{
    private readonly List<Book> _inventory = [];

    private Store()
    {
    }

    private Store(
        Guid id,
        string address,
        string name,
        string description,
        string phoneNumber,
        string? website,
        DateTimeOffset utcNow)
    {
        Id = id;
        Address = Guard.AgainstNullOrWhiteSpace(address, nameof(address));
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Description = Guard.AgainstNullOrWhiteSpace(description, nameof(description));
        PhoneNumber = Guard.AgainstNullOrWhiteSpace(phoneNumber, nameof(phoneNumber));
        Website = Guard.NormalizeOptional(website);
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public Guid Id { get; private set; }

    public string Address { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string PhoneNumber { get; private set; } = string.Empty;

    public string Website { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<Book> Inventory => _inventory.AsReadOnly();

    public static Store Create(
        string address,
        string name,
        string description,
        string phoneNumber,
        string? website,
        DateTimeOffset utcNow,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), address, name, description, phoneNumber, website, utcNow);

    public void Update(
        string address,
        string name,
        string description,
        string phoneNumber,
        string? website,
        DateTimeOffset utcNow)
    {
        Address = Guard.AgainstNullOrWhiteSpace(address, nameof(address));
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Description = Guard.AgainstNullOrWhiteSpace(description, nameof(description));
        PhoneNumber = Guard.AgainstNullOrWhiteSpace(phoneNumber, nameof(phoneNumber));
        Website = Guard.NormalizeOptional(website);
        UpdatedAtUtc = utcNow;
    }

    public void AddBook(Book book, DateTimeOffset utcNow)
    {
        if (_inventory.Any(candidate => candidate.Id == book.Id))
        {
            return;
        }

        _inventory.Add(book);
        UpdatedAtUtc = utcNow;
    }

    public void RemoveBook(Guid bookId, DateTimeOffset utcNow)
    {
        var book = _inventory.SingleOrDefault(candidate => candidate.Id == bookId);
        if (book is null)
        {
            return;
        }

        _inventory.Remove(book);
        UpdatedAtUtc = utcNow;
    }
}
