using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AuthorsBooks.Infrastructure.Telemetry;

public static class ServiceTelemetry
{
    public const string ServiceName = "AuthorsBooks.Service";
    public const string ActivitySourceName = "AuthorsBooks.Service.ActivitySource";
    public const string MeterName = "AuthorsBooks.Service.Meter";

    private static readonly ActivitySource InternalActivitySource = new(ActivitySourceName);
    private static readonly Meter InternalMeter = new(MeterName);

    public static Counter<long> RequestsStarted { get; } =
        InternalMeter.CreateCounter<long>("authorsbooks.requests.started");

    public static Counter<long> RequestsCompleted { get; } =
        InternalMeter.CreateCounter<long>("authorsbooks.requests.completed");

    public static Counter<long> RequestsFailed { get; } =
        InternalMeter.CreateCounter<long>("authorsbooks.requests.failed");

    public static Histogram<double> RequestDurationMilliseconds { get; } =
        InternalMeter.CreateHistogram<double>("authorsbooks.requests.duration", unit: "ms");

    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return InternalActivitySource.StartActivity(name, kind);
    }
}
