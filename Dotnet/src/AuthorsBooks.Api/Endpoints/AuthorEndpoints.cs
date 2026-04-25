using AuthorsBooks.Api.Contracts;
using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Authors.Commands;
using AuthorsBooks.Application.Authors.Queries;
using Microsoft.AspNetCore.Mvc;

namespace AuthorsBooks.Api.Endpoints;

public static class AuthorEndpoints
{
    public static IEndpointRouteBuilder MapAuthorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/authors").WithTags("Authors");

        group.MapGet("/", ListAuthorsAsync).WithName("ListAuthors");
        group.MapGet("/{authorId:guid}", GetAuthorByIdAsync).WithName("GetAuthorById");
        group.MapPost("/", CreateAuthorAsync).WithName("CreateAuthor");
        group.MapPut("/{authorId:guid}", UpdateAuthorAsync).WithName("UpdateAuthor");
        group.MapDelete("/{authorId:guid}", DeleteAuthorAsync).WithName("DeleteAuthor");

        return endpoints;
    }

    private static async Task<IResult> ListAuthorsAsync(
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(new ListAuthorsQuery(), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetAuthorByIdAsync(
        Guid authorId,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(new GetAuthorByIdQuery(authorId), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> CreateAuthorAsync(
        [FromBody] CreateAuthorRequest request,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(new CreateAuthorCommand(request.Name, request.Bio), cancellationToken);
        return Results.Created($"/api/v1/authors/{response.Id}", response);
    }

    private static async Task<IResult> UpdateAuthorAsync(
        Guid authorId,
        [FromBody] UpdateAuthorRequest request,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(new UpdateAuthorCommand(authorId, request.Name, request.Bio), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> DeleteAuthorAsync(
        Guid authorId,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher.Send(new DeleteAuthorCommand(authorId), cancellationToken);
        return Results.NoContent();
    }
}
