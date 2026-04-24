using AuthorsBooks.Domain.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorsBooks.Infrastructure.Persistence.Configurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("books");

        builder.HasKey(book => book.Id);

        builder.Property(book => book.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(book => book.AuthorId)
            .HasColumnName("author_id")
            .IsRequired();

        builder.Property(book => book.Title)
            .HasColumnName("title")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(book => book.PublicationYear)
            .HasColumnName("published_year");

        builder.Property(book => book.Isbn)
            .HasColumnName("isbn")
            .HasColumnType("text")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(book => book.CreatedAtUtc)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(book => book.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(book => book.AuthorId)
            .HasDatabaseName("idx_books_author_id");

        builder.HasIndex(book => book.Isbn)
            .HasDatabaseName("idx_books_isbn");
    }
}
