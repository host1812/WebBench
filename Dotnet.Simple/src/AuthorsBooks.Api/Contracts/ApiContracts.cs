using System.Text.Json.Serialization;

namespace AuthorsBooks.Api.Contracts;

public sealed record ErrorResponse(string Error);

public sealed record HealthResponse(
    string Status,
    string Service,
    DateTimeOffset Time,
    HealthChecksResponse Checks);

public sealed record HealthChecksResponse(HealthComponentResponse Database);

public sealed record HealthComponentResponse(string Status, string? Error);

public sealed record AuthorResponse(
    Guid Id,
    string Name,
    string Bio,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record BookResponse(
    Guid Id,
    [property: JsonPropertyName("author_id")] Guid AuthorId,
    string Title,
    string Isbn,
    [property: JsonPropertyName("published_year")] int? PublishedYear,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record StoreResponse(
    Guid Id,
    string Name,
    string Description,
    string Address,
    [property: JsonPropertyName("phone_number")] string PhoneNumber,
    [property: JsonPropertyName("web_site")] string? WebSite,
    BookResponse[] Books,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record CreateAuthorRequest(string? Name, string? Bio);

public sealed record UpdateAuthorRequest(string? Name, string? Bio);

public sealed record CreateBookRequest(
    [property: JsonPropertyName("author_id")] string? AuthorId,
    string? Title,
    string? Isbn,
    [property: JsonPropertyName("published_year")] int? PublishedYear);

public sealed record UpdateBookRequest(
    [property: JsonPropertyName("author_id")] string? AuthorId,
    string? Title,
    string? Isbn,
    [property: JsonPropertyName("published_year")] int? PublishedYear);
