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
            .ValueGeneratedNever();

        builder.Property(book => book.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(book => book.PublicationYear).IsRequired();

        builder.Property(book => book.Isbn)
            .HasMaxLength(50);

        builder.Property(book => book.CreatedAtUtc).IsRequired();
        builder.Property(book => book.UpdatedAtUtc).IsRequired();

        builder.HasIndex(book => book.AuthorId);
    }
}
