using System;
using AuthorsBooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AuthorsBooks.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
partial class ApplicationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.4")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity("AuthorsBooks.Domain.Authors.Author", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedNever()
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<string>("Bio")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("text")
                    .HasColumnName("bio")
                    .HasDefaultValue("");

                b.Property<DateTimeOffset>("CreatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("name");

                b.Property<DateTimeOffset>("UpdatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("Id");

                b.ToTable("authors", "public");
            });

        modelBuilder.Entity("AuthorsBooks.Domain.Books.Book", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedNever()
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<Guid>("AuthorId")
                    .HasColumnType("uuid")
                    .HasColumnName("author_id");

                b.Property<DateTimeOffset>("CreatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<string>("Isbn")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("text")
                    .HasColumnName("isbn")
                    .HasDefaultValue("");

                b.Property<int?>("PublicationYear")
                    .HasColumnType("integer")
                    .HasColumnName("published_year");

                b.Property<string>("Title")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("title");

                b.Property<DateTimeOffset>("UpdatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("Id");

                b.HasIndex("AuthorId")
                    .HasDatabaseName("idx_books_author_id");

                b.HasIndex("AuthorId", "Title")
                    .HasDatabaseName("idx_books_author_id_title");

                b.HasIndex("Isbn")
                    .HasDatabaseName("idx_books_isbn");

                b.HasIndex("Title")
                    .HasDatabaseName("idx_books_title");

                b.ToTable("books", "public");
            });

        modelBuilder.Entity("AuthorsBooks.Domain.Books.Book", b =>
            {
                b.HasOne("AuthorsBooks.Domain.Authors.Author", null)
                    .WithMany("Books")
                    .HasForeignKey("AuthorId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

        modelBuilder.Entity("AuthorsBooks.Domain.Authors.Author", b =>
            {
                b.Navigation("Books")
                    .UsePropertyAccessMode(PropertyAccessMode.Field);
            });
#pragma warning restore 612, 618
    }
}
