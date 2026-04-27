using AuthorsBooks.Api.Configuration;
using AuthorsBooks.Api.Contracts;
using AuthorsBooks.Api.Endpoints;
using AuthorsBooks.Api.ErrorHandling;
using AuthorsBooks.Api.Serialization;
using AuthorsBooks.Application;
using AuthorsBooks.Infrastructure;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;

DotEnvLoader.LoadFromRepoRoot();

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
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

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();

app.MapServiceEndpoints();

var apiV1 = app.MapGroup("/api/v1");
apiV1.MapAuthorEndpoints();
apiV1.MapBookEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var databaseMigrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
    await databaseMigrator.MigrateAsync();
}

app.Run();

public partial class Program;
