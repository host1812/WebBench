using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Domain.Stores;

namespace AuthorsBooks.Application.Stores.Queries;

public sealed record StoreResponse(
    Guid Id,
    string Address,
    string Name,
    string Description,
    string PhoneNumber,
    string Website,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<BookResponse> Inventory);

public static class StoreMappings
{
    public static StoreResponse ToResponse(this Store store, IReadOnlyDictionary<Guid, string> authorNames) =>
        new(
            store.Id,
            store.Address,
            store.Name,
            store.Description,
            store.PhoneNumber,
            store.Website,
            store.CreatedAtUtc,
            store.UpdatedAtUtc,
            store.Inventory
                .OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase)
                .Select(book => book.ToResponse(GetAuthorName(authorNames, book.AuthorId)))
                .ToArray());

    private static string GetAuthorName(IReadOnlyDictionary<Guid, string> authorNames, Guid authorId) =>
        authorNames.TryGetValue(authorId, out var authorName) ? authorName : string.Empty;
}
