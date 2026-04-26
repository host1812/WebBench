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
                () => TypedResults.Ok(new ServiceStatusResponse("AuthorsBooks.Api", "ok")))
            .WithName("GetServiceStatus");

        endpoints.MapGet("/health", GetHealthAsync)
            .WithName("GetHealth");

        return endpoints;
    }

    private static async Task<IResult> GetHealthAsync(
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var isHealthy = await IsDatabaseHealthyAsync(dbContext, cancellationToken);
        return isHealthy
            ? TypedResults.Ok(new HealthStatusResponse("healthy"))
            : TypedResults.Json(new HealthStatusResponse("unhealthy"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<bool> IsDatabaseHealthyAsync(
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

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (shouldCloseConnection && connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
