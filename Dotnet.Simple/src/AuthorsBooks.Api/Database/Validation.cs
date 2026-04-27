using AuthorsBooks.Api.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace AuthorsBooks.Api.Database;

public static class Validation
{
    private const int DefaultBookListLimit = 10_000;
    private const int MinBookListLimit = 1;
    private const int MaxBookListLimit = 100_000;
    private const int MaxAuthorBioLength = 4_000;
    private const int MaxIsbnLength = 32;

    public static bool TryValidateAuthor(CreateAuthorRequest request, out string name, out string bio, out string? error) =>
        TryValidateAuthor(request.Name, request.Bio, out name, out bio, out error);

    public static bool TryValidateAuthor(UpdateAuthorRequest request, out string name, out string bio, out string? error) =>
        TryValidateAuthor(request.Name, request.Bio, out name, out bio, out error);

    public static bool TryValidateBook(CreateBookRequest request, TimeProvider timeProvider, out BookWriteInput? input, out string? error) =>
        TryValidateBook(request.AuthorId, request.Title, request.Isbn, request.PublishedYear, timeProvider, out input, out error);

    public static bool TryValidateBook(UpdateBookRequest request, TimeProvider timeProvider, out BookWriteInput? input, out string? error) =>
        TryValidateBook(request.AuthorId, request.Title, request.Isbn, request.PublishedYear, timeProvider, out input, out error);

    public static bool TryGetLimit(IQueryCollection query, out int limit, out string? error)
    {
        var value = GetFirstQueryValue(query, "limit", "take");

        if (string.IsNullOrWhiteSpace(value))
        {
            limit = DefaultBookListLimit;
            error = null;
            return true;
        }

        if (!int.TryParse(value, out limit))
        {
            error = "invalid limit";
            return false;
        }

        if (limit < MinBookListLimit || limit > MaxBookListLimit)
        {
            error = "limit must be between 1 and 100000";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryGetOptionalGuid(IQueryCollection query, string key, string errorMessage, out Guid? value, out string? error)
    {
        var raw = GetFirstQueryValue(query, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = null;
            error = null;
            return true;
        }

        if (!Guid.TryParse(raw.Trim(), out var parsed))
        {
            value = null;
            error = errorMessage;
            return false;
        }

        value = parsed;
        error = null;
        return true;
    }

    private static bool TryValidateAuthor(string? rawName, string? rawBio, out string name, out string bio, out string? error)
    {
        name = rawName?.Trim() ?? string.Empty;
        bio = rawBio?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            error = "author name is required";
            return false;
        }

        if (bio.Length > MaxAuthorBioLength)
        {
            error = $"author bio must be {MaxAuthorBioLength} characters or fewer";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateBook(
        string? rawAuthorId,
        string? rawTitle,
        string? rawIsbn,
        int? publishedYear,
        TimeProvider timeProvider,
        out BookWriteInput? input,
        out string? error)
    {
        if (!Guid.TryParse(rawAuthorId?.Trim(), out var authorId))
        {
            input = null;
            error = "invalid author id";
            return false;
        }

        var title = rawTitle?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            input = null;
            error = "book title is required";
            return false;
        }

        var isbn = rawIsbn?.Trim() ?? string.Empty;
        if (isbn.Length > MaxIsbnLength)
        {
            input = null;
            error = $"isbn must be {MaxIsbnLength} characters or fewer";
            return false;
        }

        if (publishedYear.HasValue)
        {
            var maxYear = timeProvider.GetUtcNow().Year + 1;
            if (publishedYear.Value < 1450 || publishedYear.Value > maxYear)
            {
                input = null;
                error = "published year is out of range";
                return false;
            }
        }

        input = new BookWriteInput(authorId, title, isbn, publishedYear);
        error = null;
        return true;
    }

    private static string? GetFirstQueryValue(IQueryCollection query, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!query.TryGetValue(key, out StringValues values))
            {
                continue;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
