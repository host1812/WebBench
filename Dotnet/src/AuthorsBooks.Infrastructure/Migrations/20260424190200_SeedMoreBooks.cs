using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorsBooks.Infrastructure.Migrations;

public partial class SeedMoreBooks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MigrationSql.ReadEmbedded("AuthorsBooks.Infrastructure.Sql.003_seed_more_books.up.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MigrationSql.ReadEmbedded("AuthorsBooks.Infrastructure.Sql.003_seed_more_books.down.sql"));
    }
}
