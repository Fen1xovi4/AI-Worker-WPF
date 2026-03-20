using Microsoft.EntityFrameworkCore;
using WpfBrowserWorker.Data.Entities;

namespace WpfBrowserWorker.Data;

public class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public DbSet<StoredTask> Tasks => Set<StoredTask>();
    public DbSet<StoredAccount> Accounts => Set<StoredAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredTask>()
            .HasIndex(t => t.Status);

        modelBuilder.Entity<StoredTask>()
            .HasIndex(t => t.CreatedAt);

        modelBuilder.Entity<StoredAccount>()
            .HasIndex(a => a.Platform);
    }
}
