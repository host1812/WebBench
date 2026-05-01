using AuthorsBooks.Domain.Books;
using AuthorsBooks.Domain.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorsBooks.Infrastructure.Persistence.Configurations;

internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores");

        builder.HasKey(store => store.Id);

        builder.Property(store => store.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(store => store.Address)
            .HasColumnName("address")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(store => store.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(store => store.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(store => store.PhoneNumber)
            .HasColumnName("phone_number")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(store => store.Website)
            .HasColumnName("website")
            .HasColumnType("text")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(store => store.CreatedAtUtc)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(store => store.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(store => store.Name)
            .HasDatabaseName("idx_stores_name");

        builder.HasMany(store => store.Inventory)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "store_books",
                right => right
                    .HasOne<Book>()
                    .WithMany()
                    .HasForeignKey("book_id")
                    .OnDelete(DeleteBehavior.Cascade),
                left => left
                    .HasOne<Store>()
                    .WithMany()
                    .HasForeignKey("store_id")
                    .OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("store_books");

                    join.Property<Guid>("store_id")
                        .HasColumnName("store_id");

                    join.Property<Guid>("book_id")
                        .HasColumnName("book_id");

                    join.HasKey("store_id", "book_id");

                    join.HasIndex("book_id")
                        .HasDatabaseName("idx_store_books_book_id");
                });

        builder.Metadata.FindSkipNavigation(nameof(Store.Inventory))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
