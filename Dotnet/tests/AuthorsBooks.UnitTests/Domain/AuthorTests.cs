using AuthorsBooks.Domain.Authors;
using AuthorsBooks.Domain.Common;

namespace AuthorsBooks.UnitTests.Domain;

public sealed class AuthorTests
{
    [Fact]
    public void Author_manages_books_within_aggregate()
    {
        var utcNow = new DateTimeOffset(2024, 06, 01, 0, 0, 0, TimeSpan.Zero);
        var author = Author.Create("Ursula Le Guin", utcNow);

        var createdBook = author.AddBook("A Wizard of Earthsea", 1968, "isbn-1", utcNow);
        author.UpdateBook(createdBook.Id, "The Tombs of Atuan", 1971, "isbn-2", utcNow.AddMinutes(1));
        author.RemoveBook(createdBook.Id, utcNow.AddMinutes(2));

        Assert.Empty(author.Books);
        Assert.Equal(utcNow.AddMinutes(2), author.UpdatedAtUtc);
    }

    [Fact]
    public void Author_throws_when_book_is_missing()
    {
        var author = Author.Create("N. K. Jemisin", DateTimeOffset.UtcNow);

        var exception = Assert.Throws<DomainException>(() => author.GetBook(Guid.NewGuid()));

        Assert.Contains("was not found", exception.Message);
    }
}
