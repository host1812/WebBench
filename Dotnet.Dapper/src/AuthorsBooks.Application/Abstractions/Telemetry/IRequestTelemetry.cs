namespace AuthorsBooks.Application.Abstractions.Telemetry;

public interface IRequestTelemetry
{
    IRequestTelemetryScope Start(string requestName, string requestType);
}

public interface IRequestTelemetryScope : IDisposable
{
    void MarkSuccess();

    void MarkFailure(Exception exception);
}
