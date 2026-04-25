using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Authors.Commands;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Application.Books.Commands;
using AuthorsBooks.Application.Books.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuthorsBooks.Application.Common;

internal sealed class RequestDispatcher(IServiceProvider serviceProvider, ILogger<RequestDispatcher> logger)
    : IRequestDispatcher
{
    public async Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        logger.LogDebug("Dispatching request {RequestType}.", requestType.FullName);

        return request switch
        {
            ListAuthorsQuery typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<ListAuthorsQuery, IReadOnlyList<AuthorSummaryResponse>>>(),
                cancellationToken),
            GetAuthorByIdQuery typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<GetAuthorByIdQuery, AuthorDetailsResponse>>(),
                cancellationToken),
            CreateAuthorCommand typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<CreateAuthorCommand, AuthorDetailsResponse>>(),
                cancellationToken),
            UpdateAuthorCommand typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<UpdateAuthorCommand, AuthorDetailsResponse>>(),
                cancellationToken),
            DeleteAuthorCommand typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<DeleteAuthorCommand, Unit>>(),
                cancellationToken),
            ListBooksQuery typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<ListBooksQuery, IReadOnlyList<BookResponse>>>(),
                cancellationToken),
            GetBookByIdQuery typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<GetBookByIdQuery, BookResponse>>(),
                cancellationToken),
            CreateBookCommand typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<CreateBookCommand, BookResponse>>(),
                cancellationToken),
            UpdateBookCommand typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<UpdateBookCommand, BookResponse>>(),
                cancellationToken),
            DeleteBookCommand typedRequest => (TResult)(object)await Execute(
                typedRequest,
                serviceProvider.GetRequiredService<RequestExecutor<DeleteBookCommand, Unit>>(),
                cancellationToken),
            _ => throw new InvalidOperationException($"No request executor was registered for '{requestType.FullName}'."),
        };
    }

    private static Task<TResult> Execute<TRequest, TResult>(
        TRequest request,
        RequestExecutor<TRequest, TResult> executor,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResult> =>
        executor.Execute(request, cancellationToken);
}

internal sealed class RequestExecutor<TRequest, TResult>(
    IRequestHandler<TRequest, TResult> handler,
    IEnumerable<IRequestBehavior<TRequest, TResult>> behaviors)
    where TRequest : IRequest<TResult>
{
    public Task<TResult> Execute(TRequest request, CancellationToken cancellationToken)
    {
        RequestHandlerDelegate<TResult> next = () => handler.Handle(request, cancellationToken);

        foreach (var behavior in behaviors.Reverse())
        {
            var capturedBehavior = behavior;
            var capturedNext = next;
            next = () => capturedBehavior.Handle(request, cancellationToken, capturedNext);
        }

        return next();
    }
}
