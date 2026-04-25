using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace AuthorsBooks.Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=authorsbooks;Username=postgres;Password=postgres";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        LoadEnvironmentFromRepoRoot();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(connectionString) ? FallbackConnectionString : connectionString)
        {
            SearchPath = "public",
        };

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(
            connectionStringBuilder.ConnectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "public"));

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static void LoadEnvironmentFromRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");

            if (File.Exists(candidate))
            {
                foreach (var rawLine in File.ReadAllLines(candidate))
                {
                    var line = rawLine.Trim();

                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex].Trim();
                    var value = line[(separatorIndex + 1)..].Trim();

                    if (value.Length >= 2 &&
                        ((value.StartsWith('"') && value.EndsWith('"')) ||
                         (value.StartsWith('\'') && value.EndsWith('\''))))
                    {
                        value = value[1..^1];
                    }

                    if (!string.IsNullOrWhiteSpace(key) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, value);
                    }
                }

                return;
            }

            directory = directory.Parent;
        }
    }
}
