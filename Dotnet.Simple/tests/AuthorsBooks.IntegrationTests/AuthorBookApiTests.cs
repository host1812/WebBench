using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthorsBooks.Api.Contracts;

namespace AuthorsBooks.IntegrationTests;

public sealed class AuthorBookApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Api_health_endpoint_checks_database_connectivity()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions);
        Assert.NotNull(health);
        Assert.Equal("ok", health!.Status);
        Assert.Equal("books-service", health.Service);
        Assert.Equal("ok", health.Checks.Database.Status);
        Assert.Null(health.Checks.Database.Error);
    }

    [Fact]
    public async Task Api_authors_endpoint_returns_author_rows()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        await CreateAuthorAsync(client, "Listed Author");

        var authors = await client.GetFromJsonAsync<AuthorResponse[]>("/api/v1/authors", JsonOptions);

        Assert.NotNull(authors);
        Assert.Single(authors!);
        Assert.Equal("Listed Author", authors[0].Name);
    }

    [Fact]
    public async Task Api_supports_author_and_book_management_flow()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var author = await CreateAuthorAsync(client, "Terry Pratchett");

        var createdBookResponse = await client.PostAsJsonAsync(
            "/api/v1/books",
            new CreateBookRequest(author.Id.ToString(), "Guards! Guards!", "isbn-123", 1989),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, createdBookResponse.StatusCode);
        var book = await createdBookResponse.Content.ReadFromJsonAsync<BookResponse>(JsonOptions);
        Assert.NotNull(book);

        var listedBooks = await client.GetFromJsonAsync<BookResponse[]>("/api/v1/books", JsonOptions);
        var listedBooksByAuthor = await client.GetFromJsonAsync<BookResponse[]>($"/api/v1/authors/{author.Id}/books?limit=1", JsonOptions);
        var listedBooksByRootFilter = await client.GetFromJsonAsync<BookResponse[]>($"/api/v1/books?author_id={author.Id}&limit=1", JsonOptions);
        var fetchedAuthor = await client.GetFromJsonAsync<AuthorResponse>($"/api/v1/authors/{author.Id}", JsonOptions);

        Assert.NotNull(listedBooks);
        Assert.NotNull(listedBooksByAuthor);
        Assert.NotNull(listedBooksByRootFilter);
        Assert.NotNull(fetchedAuthor);
        Assert.Single(listedBooks!);
        Assert.Single(listedBooksByAuthor!);
        Assert.Single(listedBooksByRootFilter!);
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
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.NotNull(error);
        Assert.Equal("limit must be between 1 and 100000", error!.Error);
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

        var limitedBooks = await client.GetFromJsonAsync<BookResponse[]>("/api/v1/books?limit=1", JsonOptions);
        var filteredBooks = await client.GetFromJsonAsync<BookResponse[]>($"/api/v1/books?author_id={firstAuthor.Id}&limit=10", JsonOptions);

        Assert.NotNull(limitedBooks);
        Assert.NotNull(filteredBooks);
        Assert.Single(limitedBooks!);
        Assert.Equal(2, filteredBooks!.Length);
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

        var books = await client.GetFromJsonAsync<BookResponse[]>("/api/v1/books?take=1", JsonOptions);

        Assert.NotNull(books);
        Assert.Single(books!);
    }

    [Fact]
    public async Task Api_stores_endpoint_returns_stores_with_inventory()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var author = await CreateAuthorAsync(client, "Store Author");
        var book = await CreateBookAsync(client, author.Id, "Store Inventory Book");

        var stores = await client.GetFromJsonAsync<StoreResponse[]>("/api/v1/stores", JsonOptions);

        Assert.NotNull(stores);
        Assert.Equal(2, stores!.Length);
        Assert.Equal("North Star Books", stores[0].Name);
        Assert.NotEmpty(stores[0].Address);
        Assert.NotEmpty(stores[0].PhoneNumber);
        Assert.Contains(stores[0].Books, storeBook => storeBook.Id == book.Id);

        var fetchedStore = await client.GetFromJsonAsync<StoreResponse>($"/api/v1/stores/{stores[0].Id}", JsonOptions);

        Assert.NotNull(fetchedStore);
        Assert.Equal(stores[0].Id, fetchedStore!.Id);
        Assert.Contains(fetchedStore.Books, storeBook => storeBook.Id == book.Id);
    }

    private static async Task<AuthorResponse> CreateAuthorAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/authors", new CreateAuthorRequest(name, $"{name} bio"), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var author = await response.Content.ReadFromJsonAsync<AuthorResponse>(JsonOptions);
        Assert.NotNull(author);
        return author!;
    }

    private static async Task<BookResponse> CreateBookAsync(HttpClient client, Guid authorId, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/books",
            new CreateBookRequest(authorId.ToString(), title, $"{title}-isbn", 2000),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var book = await response.Content.ReadFromJsonAsync<BookResponse>(JsonOptions);
        Assert.NotNull(book);
        return book!;
    }
}
