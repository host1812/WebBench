using AuthorsBooks.Api.Contracts;
using AuthorsBooks.Api.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuthorsBooks.IntegrationTests;

internal sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable(
            "BOOKSVC_DATABASE_CONNECTION_STRING",
            "Host=localhost;Port=5432;Database=test;Username=test;Password=test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBooksDb>();
            services.RemoveAll<IDatabaseMigrator>();

            services.AddSingleton<IBooksDb, FakeBooksDb>();
            services.AddSingleton<IDatabaseMigrator, NoOpDatabaseMigrator>();
        });
    }

    private sealed class NoOpDatabaseMigrator : IDatabaseMigrator
    {
        public Task MigrateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBooksDb : IBooksDb
    {
        private readonly Lock syncRoot = new();
        private readonly Dictionary<Guid, AuthorState> authors = [];
        private readonly Dictionary<Guid, BookResponse> books = [];

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<AuthorResponse> CreateAuthorAsync(string name, string bio, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var author = new AuthorResponse(Guid.CreateVersion7(), name, bio, now, now);

            lock (syncRoot)
            {
                authors[author.Id] = new AuthorState(author);
            }

            return Task.FromResult(author);
        }

        public Task<AuthorResponse[]> ListAuthorsAsync(CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                return Task.FromResult(authors.Values
                    .Select(state => state.Author)
                    .OrderBy(author => author.Name, StringComparer.Ordinal)
                    .ThenBy(author => author.CreatedAt)
                    .ToArray());
            }
        }

        public Task<AuthorResponse?> GetAuthorAsync(Guid authorId, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                authors.TryGetValue(authorId, out var state);
                return Task.FromResult(state?.Author);
            }
        }

        public Task<AuthorResponse?> UpdateAuthorAsync(Guid authorId, string name, string bio, DateTimeOffset now, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                if (!authors.TryGetValue(authorId, out var state))
                {
                    return Task.FromResult<AuthorResponse?>(null);
                }

                var updated = state.Author with
                {
                    Name = name,
                    Bio = bio,
                    UpdatedAt = now,
                };

                state.Author = updated;
                return Task.FromResult<AuthorResponse?>(updated);
            }
        }

        public Task<bool> DeleteAuthorAsync(Guid authorId, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                if (!authors.Remove(authorId, out var state))
                {
                    return Task.FromResult(false);
                }

                foreach (var bookId in state.BookIds)
                {
                    books.Remove(bookId);
                }

                return Task.FromResult(true);
            }
        }

        public Task<bool> AuthorExistsAsync(Guid authorId, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                return Task.FromResult(authors.ContainsKey(authorId));
            }
        }

        public Task<BookResponse?> CreateBookAsync(BookWriteInput input, DateTimeOffset now, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                if (!authors.TryGetValue(input.AuthorId, out var state))
                {
                    return Task.FromResult<BookResponse?>(null);
                }

                var book = new BookResponse(Guid.CreateVersion7(), input.AuthorId, input.Title, input.Isbn, input.PublishedYear, now, now);
                books[book.Id] = book;
                state.BookIds.Add(book.Id);

                return Task.FromResult<BookResponse?>(book);
            }
        }

        public Task<BookResponse[]> ListBooksAsync(int limit, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                return Task.FromResult(books.Values
                    .OrderBy(book => book.Title, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray());
            }
        }

        public Task<BookResponse[]> ListBooksByAuthorAsync(Guid authorId, int limit, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                return Task.FromResult(books.Values
                    .Where(book => book.AuthorId == authorId)
                    .OrderBy(book => book.Title, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray());
            }
        }

        public Task<BookResponse?> GetBookAsync(Guid bookId, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                books.TryGetValue(bookId, out var book);
                return Task.FromResult(book);
            }
        }

        public Task<BookResponse?> UpdateBookAsync(Guid bookId, BookWriteInput input, DateTimeOffset now, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                if (!books.TryGetValue(bookId, out var existing))
                {
                    return Task.FromResult<BookResponse?>(null);
                }

                if (!authors.ContainsKey(input.AuthorId))
                {
                    return Task.FromResult<BookResponse?>(null);
                }

                if (existing.AuthorId != input.AuthorId)
                {
                    authors[existing.AuthorId].BookIds.Remove(bookId);
                    authors[input.AuthorId].BookIds.Add(bookId);
                }

                var updated = existing with
                {
                    AuthorId = input.AuthorId,
                    Title = input.Title,
                    Isbn = input.Isbn,
                    PublishedYear = input.PublishedYear,
                    UpdatedAt = now,
                };

                books[bookId] = updated;
                return Task.FromResult<BookResponse?>(updated);
            }
        }

        public Task<bool> DeleteBookAsync(Guid bookId, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                if (!books.Remove(bookId, out var book))
                {
                    return Task.FromResult(false);
                }

                authors[book.AuthorId].BookIds.Remove(bookId);
                return Task.FromResult(true);
            }
        }

        private sealed class AuthorState(AuthorResponse author)
        {
            public AuthorResponse Author { get; set; } = author;

            public HashSet<Guid> BookIds { get; } = [];
        }
    }
}
