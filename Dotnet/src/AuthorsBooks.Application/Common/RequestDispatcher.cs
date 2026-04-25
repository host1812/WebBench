using AuthorsBooks.Application.Abstractions.Cqrs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AuthorsBooks.Application.Common;

internal sealed class RequestDispatcher(IServiceProvider serviceProvider, ILogger<RequestDispatcher> logger)
    : IRequestDispatcher
{
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResultType), Type> ExecutorTypes = new();

    public async Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var executorType = ExecutorTypes.GetOrAdd(
            (requestType, typeof(TResult)),
            static key => typeof(RequestExecutor<,>).MakeGenericType(key.RequestType, key.ResultType));
        var executor = (IRequestExecutor)serviceProvider.GetRequiredService(executorType);

        logger.LogDebug("Dispatching request {RequestType}.", requestType.FullName);

        return (TResult)(await executor.ExecuteUntyped(request, cancellationToken))!;
    }
}

internal interface IRequestExecutor
{
    Task<object?> ExecuteUntyped(object request, CancellationToken cancellationToken);
}

internal sealed class RequestExecutor<TRequest, TResult>(
    IRequestHandler<TRequest, TResult> handler,
    IEnumerable<IRequestBehavior<TRequest, TResult>> behaviors)
    : IRequestExecutor
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

    public async Task<object?> ExecuteUntyped(object request, CancellationToken cancellationToken) =>
        await Execute((TRequest)request, cancellationToken);
}
