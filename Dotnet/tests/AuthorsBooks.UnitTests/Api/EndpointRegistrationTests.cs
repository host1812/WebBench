using AuthorsBooks.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace AuthorsBooks.UnitTests.Api;

public sealed class EndpointRegistrationTests
{
    [Fact]
    public void Endpoint_mapping_registers_expected_routes()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        app.MapAuthorEndpoints();
        app.MapBookEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(endpoints, route => string.Equals(route, "/authors", StringComparison.OrdinalIgnoreCase) || string.Equals(route, "/authors/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("/authors/{authorId:guid}", endpoints);
        Assert.Contains(endpoints, route => string.Equals(route, "/books", StringComparison.OrdinalIgnoreCase) || string.Equals(route, "/books/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("/books/{bookId:guid}", endpoints);
        Assert.Contains(endpoints, route => string.Equals(route, "/authors/{authorId:guid}/books", StringComparison.OrdinalIgnoreCase) || string.Equals(route, "/authors/{authorId:guid}/books/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("/authors/{authorId:guid}/books/{bookId:guid}", endpoints);
    }
}
