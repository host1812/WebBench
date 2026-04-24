using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace AuthorsBooks.Application.Common;

internal sealed class TelemetryBehavior<TRequest, TResult>(
    IRequestTelemetry telemetry,
    ILogger<TelemetryBehavior<TRequest, TResult>> logger)
    : IRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> Handle(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResult> next)
    {
        var requestName = typeof(TRequest).Name;
        var requestType = typeof(TRequest).FullName ?? requestName;

        using var scope = telemetry.Start(requestName, requestType);

        try
        {
            var result = await next();
            scope.MarkSuccess();
            logger.LogInformation("Request {RequestName} completed successfully.", requestName);
            return result;
        }
        catch (Exception exception)
        {
            scope.MarkFailure(exception);
            logger.LogError(exception, "Request {RequestName} failed.", requestName);
            throw;
        }
    }
}
