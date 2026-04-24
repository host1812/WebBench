using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Common;

namespace AuthorsBooks.Application.Books.Queries;

public sealed record GetBookByIdQuery(Guid BookId) : IQuery<BookResponse>;

internal sealed class GetBookByIdQueryHandler(IBookReadRepository bookReadRepository)
    : IRequestHandler<GetBookByIdQuery, BookResponse>
{
    public async Task<BookResponse> Handle(GetBookByIdQuery request, CancellationToken cancellationToken) =>
        await bookReadRepository.GetByIdAsync(request.BookId, cancellationToken)
        ?? throw new NotFoundException($"Book '{request.BookId}' was not found.");
}

internal sealed class GetBookByIdQueryValidator : IValidator<GetBookByIdQuery>
{
    public IReadOnlyCollection<ValidationFailure> Validate(GetBookByIdQuery request) =>
        request.BookId == Guid.Empty
            ? [new ValidationFailure(nameof(request.BookId), "Book id is required.")]
            : [];
}
