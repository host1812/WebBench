namespace AuthorsBooks.Api.Contracts;

public sealed record CreateBookRequest(string Title, int? PublicationYear, string? Isbn);

public sealed record UpdateBookRequest(string Title, int? PublicationYear, string? Isbn);
