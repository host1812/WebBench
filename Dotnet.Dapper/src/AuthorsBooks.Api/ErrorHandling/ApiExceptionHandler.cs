using AuthorsBooks.Application.Common;
using AuthorsBooks.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace AuthorsBooks.Api.ErrorHandling;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception while processing {Path}.", httpContext.Request.Path);

        switch (exception)
        {
            case RequestValidationException validationException:
                var errors = validationException.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

                await Results.ValidationProblem(errors).ExecuteAsync(httpContext);
                return true;

            case NotFoundException notFoundException:
                await Results.Problem(
                        title: "Resource was not found.",
                        detail: notFoundException.Message,
                        statusCode: StatusCodes.Status404NotFound)
                    .ExecuteAsync(httpContext);
                return true;

            case DomainException domainException:
                await Results.Problem(
                        title: "Domain validation failed.",
                        detail: domainException.Message,
                        statusCode: StatusCodes.Status400BadRequest)
                    .ExecuteAsync(httpContext);
                return true;

            default:
                await Results.Problem(
                        title: "An unexpected error occurred.",
                        detail: "The request could not be completed.",
                        statusCode: StatusCodes.Status500InternalServerError)
                    .ExecuteAsync(httpContext);
                return true;
        }
    }
}
