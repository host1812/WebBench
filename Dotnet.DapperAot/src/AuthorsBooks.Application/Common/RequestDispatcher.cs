using AuthorsBooks.Application.Abstractions.Cqrs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuthorsBooks.Application.Common;

internal sealed class RequestDispatcher(
    IServiceProvider serviceProvider,
    IEnumerable<RequestExecutorDescriptor> descriptors,
    ILogger<RequestDispatcher> logger)
    : IRequestDispatcher
{
    private readonly Dictionary<(Type RequestType, Type ResultType), Type> _executorTypes = descriptors
        .ToDictionary(
            descriptor => (descriptor.RequestType, descriptor.ResultType),
            descriptor => descriptor.ExecutorType);

    public async Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        if (!_executorTypes.TryGetValue((requestType, typeof(TResult)), out var executorType))
        {
            throw new InvalidOperationException($"No request executor is registered for '{requestType.FullName}'.");
        }

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

internal sealed record RequestExecutorDescriptor(Type RequestType, Type ResultType, Type ExecutorType);
