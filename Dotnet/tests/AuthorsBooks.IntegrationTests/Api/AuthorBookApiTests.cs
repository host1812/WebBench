using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AuthorsBooks.IntegrationTests.Api;

public sealed class AuthorBookApiTests
{
    [Fact]
    public async Task Api_supports_author_and_book_management_flow()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var createdAuthorResponse = await client.PostAsJsonAsync("/authors", new CreateAuthorRequest("Terry Pratchett", "Fantasy author"));
        Assert.Equal(HttpStatusCode.Created, createdAuthorResponse.StatusCode);
        var author = await createdAuthorResponse.Content.ReadFromJsonAsync<AuthorDetailsResponse>();
        Assert.NotNull(author);

        var createdBookResponse = await client.PostAsJsonAsync(
            $"/authors/{author!.Id}/books",
            new CreateBookRequest("Guards! Guards!", 1989, "isbn-123"));

        Assert.Equal(HttpStatusCode.Created, createdBookResponse.StatusCode);
        var book = await createdBookResponse.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(book);

        var listedBooks = await client.GetFromJsonAsync<List<BookResponse>>("/books");
        var listedBooksByAuthor = await client.GetFromJsonAsync<List<BookResponse>>($"/authors/{author.Id}/books?take=1");
        var fetchedAuthor = await client.GetFromJsonAsync<AuthorDetailsResponse>($"/authors/{author.Id}");

        Assert.NotNull(listedBooks);
        Assert.NotNull(listedBooksByAuthor);
        Assert.NotNull(fetchedAuthor);
        Assert.Single(listedBooks!);
        Assert.Single(listedBooksByAuthor!);
        Assert.Single(fetchedAuthor!.Books);
        Assert.Equal(book!.Id, listedBooks[0].Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100001)]
    public async Task Api_rejects_invalid_book_take_values(int take)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/books?take={take}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Take", out _));
    }

    private sealed record CreateAuthorRequest(string Name, string? Bio);

    private sealed record CreateBookRequest(string Title, int? PublicationYear, string? Isbn);

    private sealed record BookResponse(Guid Id, Guid AuthorId, string AuthorName, string Title, int? PublicationYear, string Isbn);

    private sealed record AuthorDetailsResponse(Guid Id, string Name, string Bio, IReadOnlyList<BookResponse> Books);
}
