using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Domain.Authors;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuthorsBooks.IntegrationTests.Api;

internal sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            var store = new InMemoryAuthorStore();

            services.RemoveAll<IAuthorRepository>();
            services.RemoveAll<IAuthorReadRepository>();
            services.RemoveAll<IBookReadRepository>();
            services.RemoveAll<IUnitOfWork>();
            services.RemoveAll<IDatabaseMigrator>();
            services.RemoveAll<IDatabaseHealthCheck>();

            services.AddSingleton(store);
            services.AddSingleton<IAuthorRepository>(store);
            services.AddSingleton<IAuthorReadRepository>(store);
            services.AddSingleton<IBookReadRepository>(store);
            services.AddSingleton<IUnitOfWork>(store);
            services.AddSingleton<IDatabaseMigrator, NoOpDatabaseMigrator>();
            services.AddSingleton<IDatabaseHealthCheck, HealthyDatabaseHealthCheck>();
        });
    }

    private sealed class NoOpDatabaseMigrator : IDatabaseMigrator
    {
        public Task MigrateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class HealthyDatabaseHealthCheck : IDatabaseHealthCheck
    {
        public Task<DatabaseHealthResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DatabaseHealthResult.Healthy());
    }

    private sealed class InMemoryAuthorStore :
        IAuthorRepository,
        IAuthorReadRepository,
        IBookReadRepository,
        IUnitOfWork
    {
        private readonly Lock _syncRoot = new();
        private readonly Dictionary<Guid, Author> _authors = [];

        public Task AddAsync(Author author, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _authors[author.Id] = author;
            }

            return Task.CompletedTask;
        }

        Task<Author?> IAuthorRepository.GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _authors.TryGetValue(authorId, out var author);
                return Task.FromResult(author);
            }
        }

        public void Remove(Author author)
        {
            lock (_syncRoot)
            {
                _authors.Remove(author.Id);
            }
        }

        Task<AuthorDetailsResponse?> IAuthorReadRepository.GetByIdAsync(Guid authorId, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _authors.TryGetValue(authorId, out var author);
                return Task.FromResult(author is null ? null : ToDetails(author));
            }
        }

        public Task<IReadOnlyList<AuthorSummaryResponse>> ListAsync(CancellationToken cancellationToken)
        {
            lock (_syncRoot)
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
        }

        public Task<BookResponse?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                var response = _authors.Values
                    .SelectMany(author => author.Books.Select(book => ToBook(author, book.Id)))
                    .SingleOrDefault(book => book.Id == bookId);

                return Task.FromResult(response);
            }
        }

        public Task<IReadOnlyList<BookResponse>> ListAsync(int take, Guid? authorId, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
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
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
