using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;

namespace AuthorsBooks.Application.Books.Queries;

public sealed record ListBooksQuery(int Take = 10_000, Guid? AuthorId = null) : IQuery<IReadOnlyList<BookResponse>>;

internal sealed class ListBooksQueryHandler(IBookReadRepository bookReadRepository)
    : IRequestHandler<ListBooksQuery, IReadOnlyList<BookResponse>>
{
    public Task<IReadOnlyList<BookResponse>> Handle(ListBooksQuery request, CancellationToken cancellationToken) =>
        bookReadRepository.ListAsync(request.Take, request.AuthorId, cancellationToken);
}

internal sealed class ListBooksQueryValidator : IValidator<ListBooksQuery>
{
    public IReadOnlyCollection<ValidationFailure> Validate(ListBooksQuery request) =>
        request.Take is < 1 or > 100_000
            ? [new ValidationFailure(nameof(request.Take), "Take must be between 1 and 100000.")]
            : [];
}
