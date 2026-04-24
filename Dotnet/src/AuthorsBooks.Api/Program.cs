using AuthorsBooks.Api.Endpoints;
using AuthorsBooks.Api.ErrorHandling;
using AuthorsBooks.Application;
using AuthorsBooks.Infrastructure;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet(
        "/",
        () => Results.Ok(new
        {
            Service = "AuthorsBooks.Api",
            Status = "ok",
        }))
    .WithName("GetServiceStatus");

app.MapAuthorEndpoints();
app.MapBookEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
