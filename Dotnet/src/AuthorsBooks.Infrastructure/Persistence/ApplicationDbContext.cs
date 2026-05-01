using AuthorsBooks.Application.Abstractions.Persistence;
using AuthorsBooks.Domain.Authors;
using AuthorsBooks.Domain.Books;
using AuthorsBooks.Domain.Stores;
using Microsoft.EntityFrameworkCore;

namespace AuthorsBooks.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<Store> Stores => Set<Store>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        base.SaveChangesAsync(cancellationToken);
}
