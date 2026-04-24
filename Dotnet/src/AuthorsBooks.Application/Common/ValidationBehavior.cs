using AuthorsBooks.Application.Abstractions.Cqrs;

namespace AuthorsBooks.Application.Common;

internal sealed class ValidationBehavior<TRequest, TResult>(IEnumerable<IValidator<TRequest>> validators)
    : IRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public Task<TResult> Handle(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResult> next)
    {
        var failures = validators
            .SelectMany(validator => validator.Validate(request))
            .ToArray();

        if (failures.Length != 0)
        {
            throw new RequestValidationException(failures);
        }

        return next();
    }
}
