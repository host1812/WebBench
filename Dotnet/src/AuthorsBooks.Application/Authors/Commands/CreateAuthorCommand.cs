using AuthorsBooks.Application.Abstractions.Cqrs;
using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Application.Authors.Queries;
using AuthorsBooks.Domain.Authors;

namespace AuthorsBooks.Application.Authors.Commands;

public sealed record CreateAuthorCommand(string Name) : ICommand<AuthorDetailsResponse>;

internal sealed class CreateAuthorCommandHandler(
    IAuthorRepository authorRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateAuthorCommand, AuthorDetailsResponse>
{
    public async Task<AuthorDetailsResponse> Handle(CreateAuthorCommand request, CancellationToken cancellationToken)
    {
        var author = Author.Create(request.Name, timeProvider.GetUtcNow());
        await authorRepository.AddAsync(author, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return author.ToDetailsResponse();
    }
}

internal sealed class CreateAuthorCommandValidator : IValidator<CreateAuthorCommand>
{
    public IReadOnlyCollection<ValidationFailure> Validate(CreateAuthorCommand request)
    {
        var failures = new List<ValidationFailure>();

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
