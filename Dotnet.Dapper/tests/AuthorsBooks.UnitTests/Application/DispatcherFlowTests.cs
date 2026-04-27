using AuthorsBooks.Application;
using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Abstractions.Telemetry;
using AuthorsBooks.Application.Authors.Commands;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Application.Books.Commands;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Application.Common;
using AuthorsBooks.Domain.Authors;
using Microsoft.Extensions.DependencyInjection;

namespace AuthorsBooks.UnitTests.Application;

public sealed class DispatcherFlowTests
{
    [Fact]
    public async Task Dispatcher_runs_author_commands_queries_and_telemetry()
    {
        var store = new InMemoryAuthorStore();
        var telemetry = new RecordingRequestTelemetry();
        await using var provider = CreateServiceProvider(store, telemetry);
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

        var created = await dispatcher.Send(new CreateAuthorCommand("Octavia Butler", "Speculative fiction"));
        var listed = await dispatcher.Send(new ListAuthorsQuery());
        var fetched = await dispatcher.Send(new GetAuthorByIdQuery(created.Id));
        var updated = await dispatcher.Send(new UpdateAuthorCommand(created.Id, "Octavia E. Butler", "Updated bio"));
        await dispatcher.Send(new DeleteAuthorCommand(created.Id));

        Assert.Single(listed);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Octavia E. Butler", updated.Name);
        await Assert.ThrowsAsync<NotFoundException>(() => dispatcher.Send(new GetAuthorByIdQuery(created.Id)));
        Assert.True(telemetry.Started >= 5);
        Assert.True(telemetry.Completed >= 5);
        Assert.Equal(3, store.SaveChangesCalls);
    }

    [Fact]
    public async Task Dispatcher_runs_book_commands_queries_with_take_filter()
    {
        var store = new InMemoryAuthorStore();
        var telemetry = new RecordingRequestTelemetry();
        await using var provider = CreateServiceProvider(store, telemetry);
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

        var author = await dispatcher.Send(new CreateAuthorCommand("Frank Herbert", null));
        var firstBook = await dispatcher.Send(new CreateBookCommand(author.Id, "Dune", 1965, "123"));
        var secondBook = await dispatcher.Send(new CreateBookCommand(author.Id, "Children of Dune", 1976, null));
        var allBooks = await dispatcher.Send(new ListBooksQuery(10_000));
        var limitedBooks = await dispatcher.Send(new ListBooksQuery(1));
        var authorBooks = await dispatcher.Send(new ListBooksQuery(10_000, author.Id));
        var fetched = await dispatcher.Send(new GetBookByIdQuery(firstBook.Id));
        var updated = await dispatcher.Send(new UpdateBookCommand(author.Id, firstBook.Id, "Dune Messiah", 1969, "456"));
        await dispatcher.Send(new DeleteBookCommand(author.Id, secondBook.Id));

        Assert.Equal(2, allBooks.Count);
        Assert.Single(limitedBooks);
        Assert.Equal(2, authorBooks.Count);
        Assert.Equal(firstBook.Id, fetched.Id);
        Assert.Equal("Dune Messiah", updated.Title);
        Assert.Single(await dispatcher.Send(new ListBooksQuery(10_000, author.Id)));
        Assert.True(telemetry.Started >= 8);
    }

    [Fact]
    public async Task Dispatcher_throws_validation_errors_for_invalid_requests()
    {
        var store = new InMemoryAuthorStore();
        await using var provider = CreateServiceProvider(store, new RecordingRequestTelemetry());
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => dispatcher.Send(new CreateAuthorCommand(" ", null)));

        Assert.Contains(exception.Errors, error => error.PropertyName == "Name");

        var booksException = await Assert.ThrowsAsync<RequestValidationException>(
            () => dispatcher.Send(new ListBooksQuery(100_001)));

        Assert.Contains(booksException.Errors, error => error.PropertyName == "Take");
    }

    private static ServiceProvider CreateServiceProvider(
        InMemoryAuthorStore store,
        RecordingRequestTelemetry telemetry)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2024, 06, 01, 0, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<IRequestTelemetry>(telemetry);
        services.AddSingleton<IAuthorRepository>(store);
        services.AddSingleton<IAuthorReadRepository>(store);
        services.AddSingleton<IBookReadRepository>(store);
        services.AddSingleton<IUnitOfWork>(store);

        return services.BuildServiceProvider();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingRequestTelemetry : IRequestTelemetry
    {
        public int Started { get; private set; }

        public int Completed { get; private set; }

        public int Failed { get; private set; }

        public IRequestTelemetryScope Start(string requestName, string requestType)
        {
            Started++;
            return new Scope(this);
        }

        private sealed class Scope(RecordingRequestTelemetry telemetry) : IRequestTelemetryScope
        {
            public void MarkSuccess() => telemetry.Completed++;

            public void MarkFailure(Exception exception) => telemetry.Failed++;

            public void Dispose()
            {
            }
        }
    }

    private sealed class InMemoryAuthorStore :
        IAuthorRepository,
        IAuthorReadRepository,
        IBookReadRepository,
        IUnitOfWork
    {
        private readonly Dictionary<Guid, Author> _authors = [];

        public int SaveChangesCalls { get; private set; }

        public Task AddAsync(Author author, CancellationToken cancellationToken)
        {
            _authors[author.Id] = author;
            return Task.CompletedTask;
        }

        Task<Author?> IAuthorRepository.GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
        {
            _authors.TryGetValue(authorId, out var author);
            return Task.FromResult(author);
        }

        public void Remove(Author author) => _authors.Remove(author.Id);

        Task<AuthorDetailsResponse?> IAuthorReadRepository.GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
        {
            _authors.TryGetValue(authorId, out var author);
            return Task.FromResult(author is null ? null : ToDetails(author));
        }

        public Task<IReadOnlyList<AuthorSummaryResponse>> ListAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<AuthorSummaryResponse> authors = _authors.Values
                .OrderBy(author => author.Name)
                .Select(author => new AuthorSummaryResponse(
                    author.Id,
                    author.Name,
                    author.Bio,
                    author.CreatedAtUtc,
                    author.UpdatedAtUtc,
                    author.Books.Count))
                .ToArray();

            return Task.FromResult(authors);
        }

        public Task<BookResponse?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken)
        {
            var response = _authors.Values
                .SelectMany(author => author.Books.Select(book => ToBook(author, book.Id)))
                .SingleOrDefault(book => book.Id == bookId);

            return Task.FromResult(response);
        }

        public Task<IReadOnlyList<BookResponse>> ListAsync(int take, Guid? authorId, CancellationToken cancellationToken)
        {
            IEnumerable<Author> authors = _authors.Values;

            if (authorId.HasValue)
            {
                authors = authors.Where(author => author.Id == authorId.Value);
            }

            IReadOnlyList<BookResponse> books = authors
                .SelectMany(author => author.Books.Select(book => ToBook(author, book.Id)))
                .OrderBy(book => book.Title)
                .Take(take)
                .ToArray();

            return Task.FromResult(books);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }

        private static AuthorDetailsResponse ToDetails(Author author) =>
            new(
                author.Id,
                author.Name,
                author.Bio,
                author.CreatedAtUtc,
                author.UpdatedAtUtc,
                author.Books
                    .OrderBy(book => book.Title)
                    .Select(book => ToBook(author, book.Id))
                    .ToArray());

        private static BookResponse ToBook(Author author, Guid bookId)
        {
            var book = author.GetBook(bookId);
            return new BookResponse(
                book.Id,
                author.Id,
                author.Name,
                book.Title,
                book.PublicationYear,
                book.Isbn,
                book.CreatedAtUtc,
                book.UpdatedAtUtc);
        }
    }
}
