using System.Data;
using AuthorsBooks.Api.Contracts;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthorsBooks.Api.Endpoints;

public static class ServiceEndpoints
{
    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/",
                () => TypedResults.Ok(new ServiceStatusResponse("AuthorsBooks.Api.Aot", "ok")))
            .WithName("GetServiceStatus");

        endpoints.MapGet("/health", GetHealthAsync)
            .WithName("GetHealth");

        return endpoints;
    }

    private static async Task<IResult> GetHealthAsync(
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var check = await CheckDatabaseHealthAsync(dbContext, cancellationToken);
        var response = new HealthStatusResponse(
            check.Status,
            "AuthorsBooks.Api.Aot",
            DateTimeOffset.UtcNow,
            new HealthChecksResponse(new HealthComponentResponse(check.Status, check.Error)));

        return check.IsHealthy
            ? TypedResults.Ok(response)
            : TypedResults.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<DatabaseHealthResult> CheckDatabaseHealthAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State == ConnectionState.Closed;

        try
        {
            if (shouldCloseConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return DatabaseHealthResult.Healthy();
        }
        catch (Exception exception)
        {
            return DatabaseHealthResult.Unhealthy(exception.Message);
        }
        finally
        {
            if (shouldCloseConnection && connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed record DatabaseHealthResult(bool IsHealthy, string Status, string? Error)
    {
        public static DatabaseHealthResult Healthy() => new(true, "healthy", null);

        public static DatabaseHealthResult Unhealthy(string error) => new(false, "unhealthy", error);
    }
}
