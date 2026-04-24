using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Common;

namespace AuthorsBooks.Application.Authors.Queries;

public sealed record GetAuthorByIdQuery(Guid AuthorId) : IQuery<AuthorDetailsResponse>;

internal sealed class GetAuthorByIdQueryHandler(IAuthorReadRepository authorReadRepository)
    : IRequestHandler<GetAuthorByIdQuery, AuthorDetailsResponse>
{
    public async Task<AuthorDetailsResponse> Handle(GetAuthorByIdQuery request, CancellationToken cancellationToken) =>
        await authorReadRepository.GetByIdAsync(request.AuthorId, cancellationToken)
        ?? throw new NotFoundException($"Author '{request.AuthorId}' was not found.");
}

internal sealed class GetAuthorByIdQueryValidator : IValidator<GetAuthorByIdQuery>
{
    public IReadOnlyCollection<ValidationFailure> Validate(GetAuthorByIdQuery request) =>
        request.AuthorId == Guid.Empty
            ? [new ValidationFailure(nameof(request.AuthorId), "Author id is required.")]
            : [];
}
