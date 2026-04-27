using System.Reflection;

namespace AuthorsBooks.Infrastructure.Persistence;

internal static class SqlScriptLoader
{
    public static string ReadEmbedded(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
