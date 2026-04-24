namespace AuthorsBooks.Api.Contracts;

public sealed record CreateAuthorRequest(string Name, string? Bio);

public sealed record UpdateAuthorRequest(string Name, string? Bio);
