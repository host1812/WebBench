using AuthorsBooks.Api.Configuration;

namespace AuthorsBooks.UnitTests.Api;

public sealed class DotEnvLoaderTests
{
    [Fact]
    public void Load_sets_missing_environment_variables_from_file()
    {
        var tempFile = Path.GetTempFileName();
        const string settingName = "ConnectionStrings__Postgres";
        const string otherSettingName = "IMAGE_NAME";

        try
        {
            File.WriteAllText(
                tempFile,
                """
                # comment
                ConnectionStrings__Postgres=Host=db;Port=5432;Database=test;Username=user;Password=pass
                IMAGE_NAME="authors-books-api"
                """);

            Environment.SetEnvironmentVariable(settingName, null);
            Environment.SetEnvironmentVariable(otherSettingName, null);

            DotEnvLoader.Load(tempFile);

            Assert.Equal("Host=db;Port=5432;Database=test;Username=user;Password=pass", Environment.GetEnvironmentVariable(settingName));
            Assert.Equal("authors-books-api", Environment.GetEnvironmentVariable(otherSettingName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(settingName, null);
            Environment.SetEnvironmentVariable(otherSettingName, null);
            File.Delete(tempFile);
        }
    }
}
