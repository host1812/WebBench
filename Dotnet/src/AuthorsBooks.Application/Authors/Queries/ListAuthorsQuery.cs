using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;

namespace AuthorsBooks.Application.Authors.Queries;

public sealed record ListAuthorsQuery() : IQuery<IReadOnlyList<AuthorSummaryResponse>>;

internal sealed class ListAuthorsQueryHandler(IAuthorReadRepository authorReadRepository)
    : IRequestHandler<ListAuthorsQuery, IReadOnlyList<AuthorSummaryResponse>>
{
    public Task<IReadOnlyList<AuthorSummaryResponse>> Handle(ListAuthorsQuery request, CancellationToken cancellationToken) =>
        authorReadRepository.ListAsync(cancellationToken);
}
