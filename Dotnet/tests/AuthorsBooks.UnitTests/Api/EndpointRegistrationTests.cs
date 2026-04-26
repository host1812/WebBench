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

        app.MapServiceEndpoints();
        var apiV1 = app.MapGroup("/api/v1");
        apiV1.MapAuthorEndpoints();
        apiV1.MapBookEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("/", endpoints);
        Assert.Contains("/health", endpoints);
        Assert.Contains(endpoints, route => string.Equals(route, "/api/v1/authors", StringComparison.OrdinalIgnoreCase) || string.Equals(route, "/api/v1/authors/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("/api/v1/authors/{authorId:guid}", endpoints);
        Assert.Contains(endpoints, route => string.Equals(route, "/api/v1/books", StringComparison.OrdinalIgnoreCase) || string.Equals(route, "/api/v1/books/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("/api/v1/books/{bookId:guid}", endpoints);
        Assert.Contains(endpoints, route => string.Equals(route, "/api/v1/authors/{authorId:guid}/books", StringComparison.OrdinalIgnoreCase) || string.Equals(route, "/api/v1/authors/{authorId:guid}/books/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("/api/v1/authors/{authorId:guid}/books/{bookId:guid}", endpoints);
    }
}
