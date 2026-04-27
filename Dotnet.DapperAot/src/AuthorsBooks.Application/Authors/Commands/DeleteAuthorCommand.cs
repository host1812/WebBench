using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Common;

namespace AuthorsBooks.Application.Authors.Commands;

public sealed record DeleteAuthorCommand(Guid AuthorId) : ICommand<Unit>;

internal sealed class DeleteAuthorCommandHandler(
    IAuthorRepository authorRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteAuthorCommand, Unit>
{
    public async Task<Unit> Handle(DeleteAuthorCommand request, CancellationToken cancellationToken)
    {
        var author = await authorRepository.GetByIdAsync(request.AuthorId, cancellationToken)
            ?? throw new NotFoundException($"Author '{request.AuthorId}' was not found.");

        authorRepository.Remove(author);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class DeleteAuthorCommandValidator : IValidator<DeleteAuthorCommand>
{
    public IReadOnlyCollection<ValidationFailure> Validate(DeleteAuthorCommand request) =>
        request.AuthorId == Guid.Empty
            ? [new ValidationFailure(nameof(request.AuthorId), "Author id is required.")]
            : [];
}
