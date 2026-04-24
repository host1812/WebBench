namespace AuthorsBooks.Domain.Common;

internal static class Guard
{
    public static string AgainstNullOrWhiteSpace(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        return value.Trim();
    }

    public static int AgainstOutOfRange(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new DomainException($"{parameterName} must be between {minimum} and {maximum}.");
        }

        return value;
    }
}
