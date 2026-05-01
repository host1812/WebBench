using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthorsBooks.Domain.Stores;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuthorsBooks.IntegrationTests.Api;

public sealed class AuthorBookApiTests
{
    [Fact]
    public async Task Api_health_endpoint_checks_database_connectivity()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<HealthStatusResponse>();
        Assert.NotNull(health);
        Assert.Equal("healthy", health!.Status);
        Assert.Equal("AuthorsBooks.Api", health.Service);
        Assert.Equal("healthy", health.Checks.Database.Status);
        Assert.Null(health.Checks.Database.Error);
    }

    [Fact]
    public async Task Api_authors_endpoint_returns_author_summaries()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        await CreateAuthorAsync(client, "Listed Author");

        var authors = await client.GetFromJsonAsync<List<AuthorSummaryResponse>>("/api/v1/authors");

        Assert.NotNull(authors);
        Assert.Single(authors!);
        Assert.Equal("Listed Author", authors[0].Name);
    }

    [Fact]
    public async Task Api_supports_author_and_book_management_flow()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var createdAuthorResponse = await client.PostAsJsonAsync("/api/v1/authors", new CreateAuthorRequest("Terry Pratchett", "Fantasy author"));
        Assert.Equal(HttpStatusCode.Created, createdAuthorResponse.StatusCode);
        var author = await createdAuthorResponse.Content.ReadFromJsonAsync<AuthorDetailsResponse>();
        Assert.NotNull(author);

        var createdBookResponse = await client.PostAsJsonAsync(
            $"/api/v1/authors/{author!.Id}/books",
            new CreateBookRequest("Guards! Guards!", 1989, "isbn-123"));

        Assert.Equal(HttpStatusCode.Created, createdBookResponse.StatusCode);
        var book = await createdBookResponse.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(book);

        var listedBooks = await client.GetFromJsonAsync<List<BookResponse>>("/api/v1/books");
        var listedBooksByAuthor = await client.GetFromJsonAsync<List<BookResponse>>($"/api/v1/authors/{author.Id}/books?limit=1");
        var listedBooksByRootFilter = await client.GetFromJsonAsync<List<BookResponse>>($"/api/v1/books?author_id={author.Id}&limit=1");
        var fetchedAuthor = await client.GetFromJsonAsync<AuthorDetailsResponse>($"/api/v1/authors/{author.Id}");

        Assert.NotNull(listedBooks);
        Assert.NotNull(listedBooksByAuthor);
        Assert.NotNull(listedBooksByRootFilter);
        Assert.NotNull(fetchedAuthor);
        Assert.Single(listedBooks!);
        Assert.Single(listedBooksByAuthor!);
        Assert.Single(listedBooksByRootFilter!);
        Assert.Single(fetchedAuthor!.Books);
        Assert.Equal(book!.Id, listedBooks[0].Id);
        Assert.Equal(book.Id, listedBooksByRootFilter[0].Id);
    }

    [Theory]
    [InlineData("limit", 0)]
    [InlineData("limit", 100001)]
    [InlineData("take", 0)]
    [InlineData("take", 100001)]
    public async Task Api_rejects_invalid_book_limit_values(string parameterName, int value)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/books?{parameterName}={value}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Take", out _));
    }

    [Fact]
    public async Task Api_books_endpoint_honors_limit_and_author_id_query_parameters()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var firstAuthor = await CreateAuthorAsync(client, "Author One");
        var secondAuthor = await CreateAuthorAsync(client, "Author Two");

        await CreateBookAsync(client, firstAuthor.Id, "Alpha");
        await CreateBookAsync(client, firstAuthor.Id, "Beta");
        await CreateBookAsync(client, secondAuthor.Id, "Gamma");

        var limitedBooks = await client.GetFromJsonAsync<List<BookResponse>>("/api/v1/books?limit=1");
        var filteredBooks = await client.GetFromJsonAsync<List<BookResponse>>($"/api/v1/books?author_id={firstAuthor.Id}&limit=10");

        Assert.NotNull(limitedBooks);
        Assert.NotNull(filteredBooks);
        Assert.Single(limitedBooks!);
        Assert.Equal(2, filteredBooks!.Count);
        Assert.All(filteredBooks, book => Assert.Equal(firstAuthor.Id, book.AuthorId));
    }

    [Fact]
    public async Task Api_books_endpoint_keeps_take_as_backward_compatible_alias()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var author = await CreateAuthorAsync(client, "Alias Author");
        await CreateBookAsync(client, author.Id, "Alpha");
        await CreateBookAsync(client, author.Id, "Beta");

        var books = await client.GetFromJsonAsync<List<BookResponse>>("/api/v1/books?take=1");

        Assert.NotNull(books);
        Assert.Single(books!);
    }

    [Fact]
    public async Task Api_stores_endpoint_returns_store_inventory()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var author = await CreateAuthorAsync(client, "Store Author");
        var book = await CreateBookAsync(client, author.Id, "Shelf Book");
        await CreateStoreAsync(factory, book.Id);

        var stores = await client.GetFromJsonAsync<List<StoreResponse>>("/api/v1/stores");

        Assert.NotNull(stores);
        var store = Assert.Single(stores!);
        Assert.Equal("Downtown Books", store.Name);
        Assert.Equal("123 Main Street", store.Address);
        Assert.Equal("Local book shop", store.Description);
        Assert.Equal("317-555-0100", store.PhoneNumber);
        Assert.Equal("https://downtown.example", store.Website);
        var inventoryBook = Assert.Single(store.Inventory);
        Assert.Equal(book.Id, inventoryBook.Id);
        Assert.Equal(author.Id, inventoryBook.AuthorId);
        Assert.Equal("Store Author", inventoryBook.AuthorName);
    }

    private static async Task<AuthorDetailsResponse> CreateAuthorAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/authors", new CreateAuthorRequest(name, $"{name} bio"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var author = await response.Content.ReadFromJsonAsync<AuthorDetailsResponse>();
        Assert.NotNull(author);
        return author!;
    }

    private static async Task<BookResponse> CreateBookAsync(HttpClient client, Guid authorId, string title)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/authors/{authorId}/books",
            new CreateBookRequest(title, 2000, $"{title}-isbn"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var book = await response.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(book);
        return book!;
    }

    private static async Task CreateStoreAsync(TestWebApplicationFactory factory, Guid bookId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var book = await dbContext.Books.SingleAsync(candidate => candidate.Id == bookId);
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(
            "123 Main Street",
            "Downtown Books",
            "Local book shop",
            "317-555-0100",
            "https://downtown.example",
            now);

        store.AddBook(book, now);
        dbContext.Stores.Add(store);
        await dbContext.SaveChangesAsync();
    }

    private sealed record CreateAuthorRequest(string Name, string? Bio);

    private sealed record CreateBookRequest(string Title, int? PublicationYear, string? Isbn);

    private sealed record HealthStatusResponse(string Status, string Service, DateTimeOffset Time, HealthChecksResponse Checks);

    private sealed record HealthChecksResponse(HealthComponentResponse Database);

    private sealed record HealthComponentResponse(string Status, string? Error);

    private sealed record AuthorSummaryResponse(Guid Id, string Name, string Bio, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int BookCount);

    private sealed record BookResponse(Guid Id, Guid AuthorId, string AuthorName, string Title, int? PublicationYear, string Isbn);

    private sealed record AuthorDetailsResponse(Guid Id, string Name, string Bio, IReadOnlyList<BookResponse> Books);

    private sealed record StoreResponse(
        Guid Id,
        string Address,
        string Name,
        string Description,
        string PhoneNumber,
        string Website,
        IReadOnlyList<BookResponse> Inventory);
}
