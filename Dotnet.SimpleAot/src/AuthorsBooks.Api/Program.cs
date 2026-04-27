using AuthorsBooks.Api.Configuration;
using AuthorsBooks.Api.Contracts;
using AuthorsBooks.Api.Database;
using AuthorsBooks.Api.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

DotEnvLoader.LoadFromRepoRoot();

var (command, remainingArgs) = AppCommandParser.Parse(args);
var builder = WebApplication.CreateSlimBuilder(remainingArgs);
var config = AppConfig.FromConfiguration(builder.Configuration, command == AppCommand.Migrate);

builder.WebHost.UseUrls(config.HttpAddress);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Logging.SetMinimumLevel(command == AppCommand.Migrate ? LogLevel.Information : LogLevel.Warning);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AuthorsBooksJsonSerializerContext.Default);
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(CreateDataSource(config));
builder.Services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
builder.Services.AddSingleton<IBooksDb, NpgsqlBooksDb>();

var app = builder.Build();

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
        logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}.",
            context.Request.Method,
            context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(
                new ErrorResponse("internal server error"),
                AuthorsBooksJsonSerializerContext.Default.ErrorResponse);
        }
    }
});

app.MapGet("/health", HealthAsync);
app.MapGet("/api/v1/health", HealthAsync);

var apiV1 = app.MapGroup("/api/v1");
apiV1.MapPost("/authors", CreateAuthorAsync);
apiV1.MapGet("/authors", ListAuthorsAsync);
apiV1.MapGet("/authors/{id:guid}", GetAuthorAsync);
apiV1.MapPut("/authors/{id:guid}", UpdateAuthorAsync);
apiV1.MapDelete("/authors/{id:guid}", DeleteAuthorAsync);
apiV1.MapGet("/authors/{id:guid}/books", ListAuthorBooksAsync);

apiV1.MapPost("/books", CreateBookAsync);
apiV1.MapGet("/books", ListBooksAsync);
apiV1.MapGet("/books/{id:guid}", GetBookAsync);
apiV1.MapPut("/books/{id:guid}", UpdateBookAsync);
apiV1.MapDelete("/books/{id:guid}", DeleteBookAsync);

await using (var scope = app.Services.CreateAsyncScope())
{
    var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
    await migrator.MigrateAsync();
}

if (command == AppCommand.Migrate)
{
    return;
}

await app.RunAsync();

static NpgsqlDataSource CreateDataSource(AppConfig config)
{
    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(config.DatabaseConnectionString)
    {
        MaxPoolSize = config.MaxConnections,
        SearchPath = "public",
        MaxAutoPrepare = 64,
        AutoPrepareMinUsages = 2,
    };

    return NpgsqlDataSource.Create(connectionStringBuilder.ConnectionString);
}

static async Task<IResult> HealthAsync(IBooksDb database, AppConfig config, CancellationToken cancellationToken)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(2));

    var isHealthy = await database.CanConnectAsync(timeout.Token);
    var status = isHealthy ? "ok" : "degraded";

    return TypedResults.Json(
        new HealthResponse(
            status,
            config.ServiceName,
            DateTimeOffset.UtcNow,
            new HealthChecksResponse(
                new HealthComponentResponse(
                    isHealthy ? "ok" : "degraded",
                    isHealthy ? null : "database ping failed"))),
        AuthorsBooksJsonSerializerContext.Default.HealthResponse,
        statusCode: isHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}

static async Task<IResult> CreateAuthorAsync(CreateAuthorRequest request, IBooksDb database, TimeProvider timeProvider, CancellationToken cancellationToken)
{
    if (!Validation.TryValidateAuthor(request, out var name, out var bio, out var error))
    {
        return TypedResults.BadRequest(new ErrorResponse(error!));
    }

    var author = await database.CreateAuthorAsync(name, bio, timeProvider.GetUtcNow(), cancellationToken);
    return TypedResults.Created($"/api/v1/authors/{author.Id}", author);
}

static async Task<AuthorResponse[]> ListAuthorsAsync(IBooksDb database, CancellationToken cancellationToken) =>
    await database.ListAuthorsAsync(cancellationToken);

static async Task<IResult> GetAuthorAsync(Guid id, IBooksDb database, CancellationToken cancellationToken)
{
    var author = await database.GetAuthorAsync(id, cancellationToken);
    return author is null
        ? TypedResults.NotFound(new ErrorResponse("resource not found"))
        : TypedResults.Ok(author);
}

static async Task<IResult> UpdateAuthorAsync(Guid id, UpdateAuthorRequest request, IBooksDb database, TimeProvider timeProvider, CancellationToken cancellationToken)
{
    if (!Validation.TryValidateAuthor(request, out var name, out var bio, out var error))
    {
        return TypedResults.BadRequest(new ErrorResponse(error!));
    }

    var author = await database.UpdateAuthorAsync(id, name, bio, timeProvider.GetUtcNow(), cancellationToken);
    return author is null
        ? TypedResults.NotFound(new ErrorResponse("resource not found"))
        : TypedResults.Ok(author);
}

static async Task<IResult> DeleteAuthorAsync(Guid id, IBooksDb database, CancellationToken cancellationToken)
{
    var deleted = await database.DeleteAuthorAsync(id, cancellationToken);
    return deleted
        ? TypedResults.NoContent()
        : TypedResults.NotFound(new ErrorResponse("resource not found"));
}

static async Task<IResult> ListAuthorBooksAsync(Guid id, HttpContext httpContext, IBooksDb database, CancellationToken cancellationToken)
{
    if (!Validation.TryGetLimit(httpContext.Request.Query, out var limit, out var error))
    {
        return TypedResults.BadRequest(new ErrorResponse(error!));
    }

    var books = await database.ListBooksByAuthorAsync(id, limit, cancellationToken);
    if (books.Length == 0 && !await database.AuthorExistsAsync(id, cancellationToken))
    {
        return TypedResults.NotFound(new ErrorResponse("resource not found"));
    }

    return TypedResults.Ok(books);
}

static async Task<IResult> CreateBookAsync(CreateBookRequest request, IBooksDb database, TimeProvider timeProvider, CancellationToken cancellationToken)
{
    if (!Validation.TryValidateBook(request, timeProvider, out var input, out var error))
    {
        return TypedResults.BadRequest(new ErrorResponse(error!));
    }

    var book = await database.CreateBookAsync(input!, timeProvider.GetUtcNow(), cancellationToken);
    return book is null
        ? TypedResults.NotFound(new ErrorResponse("resource not found"))
        : TypedResults.Created($"/api/v1/books/{book.Id}", book);
}

static async Task<IResult> ListBooksAsync(HttpContext httpContext, IBooksDb database, CancellationToken cancellationToken)
{
    if (!Validation.TryGetLimit(httpContext.Request.Query, out var limit, out var error))
    {
        return TypedResults.BadRequest(new ErrorResponse(error!));
    }

    if (!Validation.TryGetOptionalGuid(httpContext.Request.Query, "author_id", "invalid author id", out var authorId, out error))
    {
        return TypedResults.BadRequest(new ErrorResponse(error!));
    }

    var books = authorId.HasValue
        ? await database.ListBooksByAuthorAsync(authorId.Value, limit, cancellationToken)
        : await database.ListBooksAsync(limit, cancellationToken);

    return TypedResults.Ok(books);
}

static async Task<IResult> GetBookAsync(Guid id, IBooksDb database, CancellationToken cancellationToken)
{
    var book = await database.GetBookAsync(id, cancellationToken);
    return book is null
        ? TypedResults.NotFound(new ErrorResponse("resource not found"))
        : TypedResults.Ok(book);
}

static async Task<IResult> UpdateBookAsync(Guid id, UpdateBookRequest request, IBooksDb database, TimeProvider timeProvider, CancellationToken cancellationToken)
{
    if (!Validation.TryValidateBook(request, timeProvider, out var input, out var error))
    {
        return TypedResults.BadRequest(new ErrorResponse(error!));
    }

    var book = await database.UpdateBookAsync(id, input!, timeProvider.GetUtcNow(), cancellationToken);
    return book is null
        ? TypedResults.NotFound(new ErrorResponse("resource not found"))
        : TypedResults.Ok(book);
}

static async Task<IResult> DeleteBookAsync(Guid id, IBooksDb database, CancellationToken cancellationToken)
{
    var deleted = await database.DeleteBookAsync(id, cancellationToken);
    return deleted
        ? TypedResults.NoContent()
        : TypedResults.NotFound(new ErrorResponse("resource not found"));
}

public partial class Program;
