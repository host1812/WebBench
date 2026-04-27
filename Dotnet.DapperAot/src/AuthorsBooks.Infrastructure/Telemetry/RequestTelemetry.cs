using System.Diagnostics;
using AuthorsBooks.Application.Abstractions.Telemetry;

namespace AuthorsBooks.Infrastructure.Telemetry;

internal sealed class RequestTelemetry : IRequestTelemetry
{
    public IRequestTelemetryScope Start(string requestName, string requestType)
    {
        var activity = ServiceTelemetry.StartActivity(requestName, ActivityKind.Internal);
        activity?.SetTag("cqrs.request.name", requestName);
        activity?.SetTag("cqrs.request.type", requestType);

        ServiceTelemetry.RequestsStarted.Add(
            1,
            new KeyValuePair<string, object?>("request.name", requestName));

        return new RequestTelemetryScope(requestName, activity);
    }

    private sealed class RequestTelemetryScope(string requestName, Activity? activity) : IRequestTelemetryScope
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _completed;
        private string _outcome = "unknown";

        public void MarkSuccess()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _outcome = "success";
            activity?.SetStatus(ActivityStatusCode.Ok);
            ServiceTelemetry.RequestsCompleted.Add(
                1,
                new KeyValuePair<string, object?>("request.name", requestName));
        }

        public void MarkFailure(Exception exception)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _outcome = "failure";
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", exception.GetType().FullName);
            ServiceTelemetry.RequestsFailed.Add(
                1,
                new KeyValuePair<string, object?>("request.name", requestName),
                new KeyValuePair<string, object?>("exception.type", exception.GetType().Name));
        }

        public void Dispose()
        {
            _stopwatch.Stop();

            ServiceTelemetry.RequestDurationMilliseconds.Record(
                _stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("request.name", requestName),
                new KeyValuePair<string, object?>("request.outcome", _outcome));

            activity?.Dispose();
        }
    }
}
