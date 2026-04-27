namespace AuthorsBooks.Application.Abstractions.Cqrs;

public interface IValidator<in TRequest>
{
    IReadOnlyCollection<ValidationFailure> Validate(TRequest request);
}

public sealed record ValidationFailure(string PropertyName, string ErrorMessage);
