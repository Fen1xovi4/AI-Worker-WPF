using Microsoft.EntityFrameworkCore;
using WpfBrowserWorker.Data.Entities;

namespace WpfBrowserWorker.Data;

public class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public DbSet<StoredTask> Tasks => Set<StoredTask>();
    public DbSet<StoredAccount> Accounts => Set<StoredAccount>();
    public DbSet<BrowserProfile> Profiles => Set<BrowserProfile>();
    public DbSet<ProfilePage> ProfilePages => Set<ProfilePage>();
    public DbSet<TelegramBotConfig> TelegramBots => Set<TelegramBotConfig>();
    public DbSet<PublishLog> PublishLogs => Set<PublishLog>();
    public DbSet<LocalScheduledTask> LocalScheduledTasks => Set<LocalScheduledTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredTask>()
            .HasIndex(t => t.Status);

        modelBuilder.Entity<StoredTask>()
            .HasIndex(t => t.CreatedAt);

        modelBuilder.Entity<StoredAccount>()
            .HasIndex(a => a.Platform);

        modelBuilder.Entity<BrowserProfile>()
            .ToTable("BrowserProfiles");

        modelBuilder.Entity<BrowserProfile>()
            .HasIndex(p => p.Name)
            .IsUnique();

        modelBuilder.Entity<BrowserProfile>()
            .HasIndex(p => p.LinkedAccountId);

        modelBuilder.Entity<BrowserProfile>()
            .HasIndex(p => p.Status);

        modelBuilder.Entity<StoredAccount>()
            .HasIndex(a => a.LinkedProfileId);

        modelBuilder.Entity<ProfilePage>()
            .ToTable("ProfilePages");

        modelBuilder.Entity<ProfilePage>()
            .HasOne(pp => pp.Account)
            .WithMany()
            .HasForeignKey(pp => pp.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProfilePage>()
            .HasIndex(pp => pp.AccountId);

        modelBuilder.Entity<TelegramBotConfig>()
            .ToTable("TelegramBotConfigs");

        modelBuilder.Entity<TelegramBotConfig>()
            .HasOne(t => t.Account)
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TelegramBotConfig>()
            .HasIndex(t => t.AccountId)
            .IsUnique(); // one bot per account
    }
}
