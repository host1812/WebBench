using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorsBooks.Infrastructure.Migrations;

public partial class InitialSharedSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "authors",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                bio = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_authors", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "books",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                author_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                published_year = table.Column<int>(type: "integer", nullable: true),
                isbn = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_books", x => x.id);
                table.ForeignKey(
                    name: "FK_books_authors_author_id",
                    column: x => x.author_id,
                    principalTable: "authors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "idx_books_author_id",
            table: "books",
            column: "author_id");

        migrationBuilder.CreateIndex(
            name: "idx_books_isbn",
            table: "books",
            column: "isbn");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "books");

        migrationBuilder.DropTable(
            name: "authors");
    }
}
