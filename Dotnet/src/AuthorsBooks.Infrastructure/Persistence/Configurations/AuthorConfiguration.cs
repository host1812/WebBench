using AuthorsBooks.Domain.Authors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorsBooks.Infrastructure.Persistence.Configurations;

internal sealed class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.ToTable("authors");

        builder.HasKey(author => author.Id);

        builder.Property(author => author.Id)
            .ValueGeneratedNever();

        builder.Property(author => author.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(author => author.CreatedAtUtc).IsRequired();
        builder.Property(author => author.UpdatedAtUtc).IsRequired();

        builder.HasMany(author => author.Books)
            .WithOne()
            .HasForeignKey(book => book.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Author.Books))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
