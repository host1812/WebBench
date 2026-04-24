namespace AuthorsBooks.Api.Contracts;

public sealed record CreateAuthorRequest(string Name);

public sealed record UpdateAuthorRequest(string Name);
