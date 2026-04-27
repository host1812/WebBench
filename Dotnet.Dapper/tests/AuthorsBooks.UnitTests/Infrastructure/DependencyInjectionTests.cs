using AuthorsBooks.Application;
using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Infrastructure;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AuthorsBooks.UnitTests.Infrastructure;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task AddApplication_registers_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IAuthorRepository, NullAuthorStore>();
        services.AddSingleton<IAuthorReadRepository, NullAuthorStore>();
        services.AddSingleton<IBookReadRepository, NullAuthorStore>();
        services.AddSingleton<IUnitOfWork, NullAuthorStore>();
        services.AddSingleton<AuthorsBooks.Application.Abstractions.Telemetry.IRequestTelemetry, NullTelemetry>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IRequestDispatcher>());
    }

    [Fact]
    public async Task AddInfrastructure_registers_database_and_repositories()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:Postgres", "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>("ConnectionStrings:ApplicationInsights", string.Empty),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<NpgsqlDataSource>());
        Assert.NotNull(provider.GetService<IAuthorRepository>());
        Assert.NotNull(provider.GetService<IAuthorReadRepository>());
        Assert.NotNull(provider.GetService<IBookReadRepository>());
        Assert.NotNull(provider.GetService<IDatabaseMigrator>());
        Assert.NotNull(provider.GetService<IDatabaseHealthCheck>());
    }

    [Fact]
    public async Task AddInfrastructure_applies_database_max_connections_to_npgsql_pool()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:Postgres", "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>("Database:MaxConnections", "12"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        await using var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);

        Assert.Equal(12, connectionStringBuilder.MaxPoolSize);
    }

    [Fact]
    public void AddInfrastructure_requires_otlp_endpoint_when_telemetry_is_enabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:Postgres", "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>("Telemetry:Enabled", "true"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddInfrastructure(configuration));
        Assert.Contains("Telemetry:OtlpEndpoint", exception.Message);
    }

    private sealed class NullTelemetry : AuthorsBooks.Application.Abstractions.Telemetry.IRequestTelemetry
    {
        public AuthorsBooks.Application.Abstractions.Telemetry.IRequestTelemetryScope Start(string requestName, string requestType) =>
            new Scope();

        private sealed class Scope : AuthorsBooks.Application.Abstractions.Telemetry.IRequestTelemetryScope
        {
            public void MarkSuccess()
            {
            }

            public void MarkFailure(Exception exception)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class NullAuthorStore :
        IAuthorRepository,
        IAuthorReadRepository,
        IBookReadRepository,
        IUnitOfWork
    {
        public Task AddAsync(AuthorsBooks.Domain.Authors.Author author, CancellationToken cancellationToken) => Task.CompletedTask;

        Task<AuthorsBooks.Domain.Authors.Author?> IAuthorRepository.GetByIdAsync(Guid authorId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthorsBooks.Domain.Authors.Author?>(null);

        public void Remove(AuthorsBooks.Domain.Authors.Author author)
        {
        }

        Task<AuthorsBooks.Application.Authors.Queries.AuthorDetailsResponse?> IAuthorReadRepository.GetByIdAsync(Guid authorId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthorsBooks.Application.Authors.Queries.AuthorDetailsResponse?>(null);

        public Task<IReadOnlyList<AuthorsBooks.Application.Authors.Queries.AuthorSummaryResponse>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuthorsBooks.Application.Authors.Queries.AuthorSummaryResponse>>([]);

        public Task<AuthorsBooks.Application.Books.Queries.BookResponse?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthorsBooks.Application.Books.Queries.BookResponse?>(null);

        public Task<IReadOnlyList<AuthorsBooks.Application.Books.Queries.BookResponse>> ListAsync(int take, Guid? authorId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuthorsBooks.Application.Books.Queries.BookResponse>>([]);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
