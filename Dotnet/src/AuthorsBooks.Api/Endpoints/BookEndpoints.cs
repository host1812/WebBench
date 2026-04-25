using AuthorsBooks.Api.Contracts;
using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Books.Commands;
using AuthorsBooks.Application.Books.Queries;
using Microsoft.AspNetCore.Mvc;

namespace AuthorsBooks.Api.Endpoints;

public static class BookEndpoints
{
    public static IEndpointRouteBuilder MapBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var booksGroup = endpoints.MapGroup("/books").WithTags("Books");
        booksGroup.MapGet("/", ListBooksAsync).WithName("ListBooks");
        booksGroup.MapGet("/{bookId:guid}", GetBookByIdAsync).WithName("GetBookById");

        var authorBooksGroup = endpoints.MapGroup("/authors/{authorId:guid}/books").WithTags("Books");
        authorBooksGroup.MapGet("/", ListBooksByAuthorAsync).WithName("ListBooksByAuthor");
        authorBooksGroup.MapPost("/", CreateBookAsync).WithName("CreateBook");
        authorBooksGroup.MapPut("/{bookId:guid}", UpdateBookAsync).WithName("UpdateBook");
        authorBooksGroup.MapDelete("/{bookId:guid}", DeleteBookAsync).WithName("DeleteBook");

        return endpoints;
    }

    private static async Task<IResult> ListBooksAsync(
        int? take,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(new ListBooksQuery(take ?? 10_000), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetBookByIdAsync(
        Guid bookId,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(new GetBookByIdQuery(bookId), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> ListBooksByAuthorAsync(
        Guid authorId,
        int? take,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(
            new ListBooksQuery(take ?? 10_000, authorId),
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateBookAsync(
        Guid authorId,
        [FromBody] CreateBookRequest request,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(
            new CreateBookCommand(authorId, request.Title, request.PublicationYear, request.Isbn),
            cancellationToken);

        return Results.Created($"/api/v1/books/{response.Id}", response);
    }

    private static async Task<IResult> UpdateBookAsync(
        Guid authorId,
        Guid bookId,
        [FromBody] UpdateBookRequest request,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.Send(
            new UpdateBookCommand(authorId, bookId, request.Title, request.PublicationYear, request.Isbn),
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> DeleteBookAsync(
        Guid authorId,
        Guid bookId,
        [FromServices] IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher.Send(new DeleteBookCommand(authorId, bookId), cancellationToken);
        return Results.NoContent();
    }
}
