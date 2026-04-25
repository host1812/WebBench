using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorsBooks.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260425150000_AddBookListIndexes")]
public partial class AddBookListIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "idx_books_author_id_title",
            table: "books",
            columns: new[] { "author_id", "title" });

        migrationBuilder.CreateIndex(
            name: "idx_books_title",
            table: "books",
            column: "title");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_books_author_id_title",
            table: "books");

        migrationBuilder.DropIndex(
            name: "idx_books_title",
            table: "books");
    }
}
