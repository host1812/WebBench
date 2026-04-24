using AuthorsBooks.Application.Abstractions.Cqrs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuthorsBooks.Application.Common;

internal sealed class RequestDispatcher(IServiceProvider serviceProvider, ILogger<RequestDispatcher> logger)
    : IRequestDispatcher
{
    public async Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executorType = typeof(RequestExecutor<,>).MakeGenericType(request.GetType(), typeof(TResult));
        dynamic executor = serviceProvider.GetRequiredService(executorType);

        logger.LogDebug("Dispatching request {RequestType}.", request.GetType().FullName);

        return await executor.Execute((dynamic)request, cancellationToken);
    }
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
