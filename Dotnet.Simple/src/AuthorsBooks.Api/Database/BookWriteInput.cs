namespace AuthorsBooks.Api.Database;

public sealed record BookWriteInput(Guid AuthorId, string Title, string Isbn, int? PublishedYear);
