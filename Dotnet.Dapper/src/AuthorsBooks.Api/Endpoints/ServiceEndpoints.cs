using AuthorsBooks.Api.Contracts;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

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
        [FromServices] IDatabaseHealthCheck databaseHealthCheck,
        CancellationToken cancellationToken)
    {
        var check = await databaseHealthCheck.CheckAsync(cancellationToken);
        var response = new HealthStatusResponse(
            check.Status,
            "AuthorsBooks.Api",
            DateTimeOffset.UtcNow,
            new HealthChecksResponse(new HealthComponentResponse(check.Status, check.Error)));

        return check.IsHealthy
            ? TypedResults.Ok(response)
            : TypedResults.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
