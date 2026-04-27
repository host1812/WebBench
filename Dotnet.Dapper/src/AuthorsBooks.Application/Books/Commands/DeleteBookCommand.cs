using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Common;
using AuthorsBooks.Domain.Common;

namespace AuthorsBooks.Application.Books.Commands;

public sealed record DeleteBookCommand(Guid AuthorId, Guid BookId) : ICommand<Unit>;

internal sealed class DeleteBookCommandHandler(
    IAuthorRepository authorRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<DeleteBookCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBookCommand request, CancellationToken cancellationToken)
    {
        var author = await authorRepository.GetByIdAsync(request.AuthorId, cancellationToken)
            ?? throw new NotFoundException($"Author '{request.AuthorId}' was not found.");

        try
        {
            author.RemoveBook(request.BookId, timeProvider.GetUtcNow());
        }
        catch (DomainException exception)
        {
            throw new NotFoundException(exception.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class DeleteBookCommandValidator : IValidator<DeleteBookCommand>
{
    public IReadOnlyCollection<ValidationFailure> Validate(DeleteBookCommand request)
    {
        var failures = new List<ValidationFailure>();

        if (request.AuthorId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(nameof(request.AuthorId), "Author id is required."));
        }

        if (request.BookId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(nameof(request.BookId), "Book id is required."));
        }

        return failures;
    }
}
