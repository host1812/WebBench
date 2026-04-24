using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Application.Common;

namespace AuthorsBooks.Application.Authors.Commands;

public sealed record UpdateAuthorCommand(Guid AuthorId, string Name) : ICommand<AuthorDetailsResponse>;

internal sealed class UpdateAuthorCommandHandler(
    IAuthorRepository authorRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateAuthorCommand, AuthorDetailsResponse>
{
    public async Task<AuthorDetailsResponse> Handle(UpdateAuthorCommand request, CancellationToken cancellationToken)
    {
        var author = await authorRepository.GetByIdAsync(request.AuthorId, cancellationToken)
            ?? throw new NotFoundException($"Author '{request.AuthorId}' was not found.");

        author.Rename(request.Name, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return author.ToDetailsResponse();
    }
}

internal sealed class UpdateAuthorCommandValidator : IValidator<UpdateAuthorCommand>
{
    public IReadOnlyCollection<ValidationFailure> Validate(UpdateAuthorCommand request)
    {
        var failures = new List<ValidationFailure>();

        if (request.AuthorId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(nameof(request.AuthorId), "Author id is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            failures.Add(new ValidationFailure(nameof(request.Name), "Author name is required."));
        }
        else if (request.Name.Trim().Length > 200)
        {
            failures.Add(new ValidationFailure(nameof(request.Name), "Author name must be 200 characters or fewer."));
        }

        return failures;
    }
}
