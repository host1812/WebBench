using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Stores.Queries;
using Microsoft.AspNetCore.Mvc;

namespace AuthorsBooks.Api.Endpoints;

public static class StoreEndpoints
{
    public static IEndpointRouteBuilder MapStoreEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/stores").WithTags("Stores");

        group.MapGet("/", ListStoresAsync).WithName("ListStores");

        return endpoints;
    }

    private static async Task<IResult> ListStoresAsync(
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(new ListStoresQuery(), cancellationToken);
        return TypedResults.Ok(response.ToArray());
    }
}
