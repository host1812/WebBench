using System.Diagnostics.CodeAnalysis;
using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Authors.Commands;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Application.Books.Commands;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace AuthorsBooks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRequestDispatcher, RequestDispatcher>();
        services.AddScoped(typeof(IRequestBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IRequestBehavior<,>), typeof(TelemetryBehavior<,>));

        RegisterRequest<ListAuthorsQuery, IReadOnlyList<AuthorSummaryResponse>, ListAuthorsQueryHandler>(services);
        RegisterRequest<GetAuthorByIdQuery, AuthorDetailsResponse, GetAuthorByIdQueryHandler>(services);
        RegisterRequest<CreateAuthorCommand, AuthorDetailsResponse, CreateAuthorCommandHandler>(services);
        RegisterRequest<UpdateAuthorCommand, AuthorDetailsResponse, UpdateAuthorCommandHandler>(services);
        RegisterRequest<DeleteAuthorCommand, Unit, DeleteAuthorCommandHandler>(services);
        RegisterRequest<ListBooksQuery, IReadOnlyList<BookResponse>, ListBooksQueryHandler>(services);
        RegisterRequest<GetBookByIdQuery, BookResponse, GetBookByIdQueryHandler>(services);
        RegisterRequest<CreateBookCommand, BookResponse, CreateBookCommandHandler>(services);
        RegisterRequest<UpdateBookCommand, BookResponse, UpdateBookCommandHandler>(services);
        RegisterRequest<DeleteBookCommand, Unit, DeleteBookCommandHandler>(services);

        services.AddScoped<IValidator<GetAuthorByIdQuery>, GetAuthorByIdQueryValidator>();
        services.AddScoped<IValidator<CreateAuthorCommand>, CreateAuthorCommandValidator>();
        services.AddScoped<IValidator<UpdateAuthorCommand>, UpdateAuthorCommandValidator>();
        services.AddScoped<IValidator<DeleteAuthorCommand>, DeleteAuthorCommandValidator>();
        services.AddScoped<IValidator<ListBooksQuery>, ListBooksQueryValidator>();
        services.AddScoped<IValidator<GetBookByIdQuery>, GetBookByIdQueryValidator>();
        services.AddScoped<IValidator<CreateBookCommand>, CreateBookCommandValidator>();
        services.AddScoped<IValidator<UpdateBookCommand>, UpdateBookCommandValidator>();
        services.AddScoped<IValidator<DeleteBookCommand>, DeleteBookCommandValidator>();

        return services;
    }

    private static void RegisterRequest<TRequest, TResult,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(IServiceCollection services)
        where TRequest : class, IRequest<TResult>
        where THandler : class, IRequestHandler<TRequest, TResult>
    {
        services.AddScoped<IRequestHandler<TRequest, TResult>, THandler>();
        services.AddScoped<RequestExecutor<TRequest, TResult>>();
        services.AddSingleton(new RequestExecutorDescriptor(
            typeof(TRequest),
            typeof(TResult),
            typeof(RequestExecutor<TRequest, TResult>)));
    }
}
