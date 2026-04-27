using System.Text.Json.Serialization;
using AuthorsBooks.Api.Contracts;

namespace AuthorsBooks.Api.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(HealthChecksResponse))]
[JsonSerializable(typeof(HealthComponentResponse))]
[JsonSerializable(typeof(AuthorResponse))]
[JsonSerializable(typeof(AuthorResponse[]))]
[JsonSerializable(typeof(BookResponse))]
[JsonSerializable(typeof(BookResponse[]))]
[JsonSerializable(typeof(CreateAuthorRequest))]
[JsonSerializable(typeof(UpdateAuthorRequest))]
[JsonSerializable(typeof(CreateBookRequest))]
[JsonSerializable(typeof(UpdateBookRequest))]
internal sealed partial class AuthorsBooksJsonSerializerContext : JsonSerializerContext
{
}
