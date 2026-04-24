using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Books.Queries;
using AuthorsBooks.Application.Common;
using AuthorsBooks.Domain.Common;

namespace AuthorsBooks.Application.Books.Commands;

public sealed record UpdateBookCommand(Guid AuthorId, Guid BookId, string Title, int PublicationYear, string? Isbn)
    : ICommand<BookResponse>;

internal sealed class UpdateBookCommandHandler(
    IAuthorRepository authorRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateBookCommand, BookResponse>
{
    public async Task<BookResponse> Handle(UpdateBookCommand request, CancellationToken cancellationToken)
    {
        var author = await authorRepository.GetByIdAsync(request.AuthorId, cancellationToken)
            ?? throw new NotFoundException($"Author '{request.AuthorId}' was not found.");

        try
        {
            author.UpdateBook(request.BookId, request.Title, request.PublicationYear, request.Isbn, timeProvider.GetUtcNow());
        }
        catch (DomainException exception)
        {
            throw new NotFoundException(exception.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return author.GetBook(request.BookId).ToResponse(author.Name);
    }
}

internal sealed class UpdateBookCommandValidator : IValidator<UpdateBookCommand>
{
    public IReadOnlyCollection<ValidationFailure> Validate(UpdateBookCommand request)
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

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            failures.Add(new ValidationFailure(nameof(request.Title), "Book title is required."));
        }
        else if (request.Title.Trim().Length > 200)
        {
            failures.Add(new ValidationFailure(nameof(request.Title), "Book title must be 200 characters or fewer."));
        }

        if (request.PublicationYear is < 1 or > 9999)
        {
            failures.Add(new ValidationFailure(nameof(request.PublicationYear), "Publication year must be between 1 and 9999."));
        }

        if (request.Isbn is { Length: > 50 })
        {
            failures.Add(new ValidationFailure(nameof(request.Isbn), "ISBN must be 50 characters or fewer."));
        }

        return failures;
    }
}
