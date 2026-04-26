namespace AuthorsBooks.Api.Contracts;

public sealed record ServiceStatusResponse(string Service, string Status);

public sealed record HealthStatusResponse(string Status);
