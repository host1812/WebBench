using AuthorsBooks.Api.Configuration;
using AuthorsBooks.Api.Endpoints;
using AuthorsBooks.Api.ErrorHandling;
using AuthorsBooks.Application;
using AuthorsBooks.Infrastructure;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

DotEnvLoader.LoadFromRepoRoot();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();

app.MapGet(
        "/",
        () => Results.Ok(new
        {
            Service = "AuthorsBooks.Api",
            Status = "ok",
        }))
    .WithName("GetServiceStatus");

app.MapGet(
        "/health",
        () => Results.Ok(new
        {
            Status = "healthy",
        }))
    .WithName("GetHealth");

app.MapAuthorEndpoints();
app.MapBookEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (dbContext.Database.IsNpgsql())
    {
        await MigrationBootstrapper.BaselineExistingSharedSchemaAsync(dbContext);
        await dbContext.Database.MigrateAsync();
    }
    else
    {
        await dbContext.Database.EnsureCreatedAsync();
    }
}

app.Run();

public partial class Program;
