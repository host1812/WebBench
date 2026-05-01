using System;
using System.Collections.Generic;
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

        modelBuilder.Entity("AuthorsBooks.Domain.Stores.Store", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedNever()
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<string>("Address")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("address");

                b.Property<DateTimeOffset>("CreatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<string>("Description")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("description");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("name");

                b.Property<string>("PhoneNumber")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("phone_number");

                b.Property<DateTimeOffset>("UpdatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.Property<string>("Website")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("text")
                    .HasColumnName("website")
                    .HasDefaultValue("");

                b.HasKey("Id");

                b.HasIndex("Name")
                    .HasDatabaseName("idx_stores_name");

                b.ToTable("stores", "public");
            });

        modelBuilder.SharedTypeEntity<Dictionary<string, object>>("store_books", b =>
            {
                b.Property<Guid>("store_id")
                    .HasColumnType("uuid")
                    .HasColumnName("store_id");

                b.Property<Guid>("book_id")
                    .HasColumnType("uuid")
                    .HasColumnName("book_id");

                b.HasKey("store_id", "book_id");

                b.HasIndex("book_id")
                    .HasDatabaseName("idx_store_books_book_id");

                b.ToTable("store_books", "public");
            });

        modelBuilder.Entity("AuthorsBooks.Domain.Books.Book", b =>
            {
                b.HasOne("AuthorsBooks.Domain.Authors.Author", null)
                    .WithMany("Books")
                    .HasForeignKey("AuthorId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

        modelBuilder.SharedTypeEntity<Dictionary<string, object>>("store_books", b =>
            {
                b.HasOne("AuthorsBooks.Domain.Books.Book", null)
                    .WithMany()
                    .HasForeignKey("book_id")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.HasOne("AuthorsBooks.Domain.Stores.Store", null)
                    .WithMany("Inventory")
                    .HasForeignKey("store_id")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

        modelBuilder.Entity("AuthorsBooks.Domain.Authors.Author", b =>
            {
                b.Navigation("Books")
                    .UsePropertyAccessMode(PropertyAccessMode.Field);
            });

        modelBuilder.Entity("AuthorsBooks.Domain.Stores.Store", b =>
            {
                b.Navigation("Inventory")
                    .UsePropertyAccessMode(PropertyAccessMode.Field);
            });
#pragma warning restore 612, 618
    }
}
