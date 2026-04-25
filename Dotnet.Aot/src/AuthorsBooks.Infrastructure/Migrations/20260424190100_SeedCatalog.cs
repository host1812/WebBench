using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorsBooks.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260424190100_SeedCatalog")]
public partial class SeedCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MigrationSql.ReadEmbedded("AuthorsBooks.Infrastructure.Sql.002_seed_catalog.up.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MigrationSql.ReadEmbedded("AuthorsBooks.Infrastructure.Sql.002_seed_catalog.down.sql"));
    }
}
