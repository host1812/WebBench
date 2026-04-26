using System.Text.Json.Serialization;
using AuthorsBooks.Api.Contracts;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Application.Books.Queries;
using Microsoft.AspNetCore.Mvc;

namespace AuthorsBooks.Api.Serialization;

[JsonSerializable(typeof(CreateAuthorRequest))]
[JsonSerializable(typeof(UpdateAuthorRequest))]
[JsonSerializable(typeof(CreateBookRequest))]
[JsonSerializable(typeof(UpdateBookRequest))]
[JsonSerializable(typeof(ServiceStatusResponse))]
[JsonSerializable(typeof(HealthStatusResponse))]
[JsonSerializable(typeof(HealthChecksResponse))]
[JsonSerializable(typeof(HealthComponentResponse))]
[JsonSerializable(typeof(AuthorSummaryResponse))]
[JsonSerializable(typeof(AuthorSummaryResponse[]))]
[JsonSerializable(typeof(AuthorDetailsResponse))]
[JsonSerializable(typeof(BookResponse))]
[JsonSerializable(typeof(BookResponse[]))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class AuthorsBooksJsonSerializerContext : JsonSerializerContext
{
}
