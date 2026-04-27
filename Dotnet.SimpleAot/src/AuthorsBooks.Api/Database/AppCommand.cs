namespace AuthorsBooks.Api.Database;

public enum AppCommand
{
    Serve,
    Migrate,
}

public static class AppCommandParser
{
    public static (AppCommand Command, string[] RemainingArgs) Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return (AppCommand.Serve, args);
        }

        return args[0].ToLowerInvariant() switch
        {
            "serve" => (AppCommand.Serve, args[1..]),
            "migrate" => (AppCommand.Migrate, args[1..]),
            _ => (AppCommand.Serve, args),
        };
    }
}
