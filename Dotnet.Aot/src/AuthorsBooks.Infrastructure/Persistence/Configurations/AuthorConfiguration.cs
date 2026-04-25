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
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(author => author.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(author => author.Bio)
            .HasColumnName("bio")
            .HasColumnType("text")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(author => author.CreatedAtUtc)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(author => author.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasMany(author => author.Books)
            .WithOne()
            .HasForeignKey(book => book.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Author.Books))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
