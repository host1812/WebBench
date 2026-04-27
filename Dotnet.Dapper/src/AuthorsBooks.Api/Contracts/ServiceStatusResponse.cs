namespace AuthorsBooks.Api.Contracts;

public sealed record ServiceStatusResponse(string Service, string Status);

public sealed record HealthStatusResponse(
    string Status,
    string Service,
    DateTimeOffset Time,
    HealthChecksResponse Checks);

public sealed record HealthChecksResponse(HealthComponentResponse Database);

public sealed record HealthComponentResponse(string Status, string? Error = null);
