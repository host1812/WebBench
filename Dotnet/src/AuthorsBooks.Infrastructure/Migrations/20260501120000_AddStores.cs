using System;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorsBooks.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260501120000_AddStores")]
public partial class AddStores : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "stores",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                address = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                phone_number = table.Column<string>(type: "text", nullable: false),
                website = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stores", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "store_books",
            columns: table => new
            {
                store_id = table.Column<Guid>(type: "uuid", nullable: false),
                book_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_store_books", x => new { x.store_id, x.book_id });
                table.ForeignKey(
                    name: "FK_store_books_books_book_id",
                    column: x => x.book_id,
                    principalTable: "books",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_store_books_stores_store_id",
                    column: x => x.store_id,
                    principalTable: "stores",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "idx_store_books_book_id",
            table: "store_books",
            column: "book_id");

        migrationBuilder.CreateIndex(
            name: "idx_stores_name",
            table: "stores",
            column: "name");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "store_books");

        migrationBuilder.DropTable(
            name: "stores");
    }
}
