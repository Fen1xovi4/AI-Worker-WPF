using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WpfBrowserWorker.Data;
using WpfBrowserWorker.Data.Entities;

namespace WpfBrowserWorker.Services;

public class ProfileService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProfileService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    // ── Chrome Profiles ────────────────────────────────────────────────────

    public async Task<BrowserProfile> CreateProfileAsync(string name, string? proxy = null, string? notes = null)
    {
        var sanitized = SanitizeName(name);
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var profilePath = Path.Combine(desktopPath, "ChromiumProfiles", sanitized);

        Directory.CreateDirectory(profilePath);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        var entity = new BrowserProfile
        {
            Name = name,
            ProfilePath = profilePath,
            Proxy = string.IsNullOrWhiteSpace(proxy) ? null : proxy.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        db.Profiles.Add(entity);
        await db.SaveChangesAsync();

        Log.Information("Created browser profile '{Name}' at {Path}", name, profilePath);
        return entity;
    }

    public async Task<List<BrowserProfile>> GetAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        return await db.Profiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
    }

    public async Task DeleteProfileAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.Profiles.FindAsync(id);
        if (entity is null) return;
        db.Profiles.Remove(entity);
        await db.SaveChangesAsync();
        Log.Information("Removed browser profile '{Name}' (id={Id}) from DB. Folder kept on disk.", entity.Name, id);
    }

    // kept for backward compat inside ProfilesViewModel
    public Task DeleteAsync(int id) => DeleteProfileAsync(id);

    public async Task UpdateLastUsedAsync(int profileId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.Profiles.FindAsync(profileId);
        if (entity is null) return;
        entity.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Resolves Chrome user-data-dir + proxy for a given account.
    /// Priority: account.LinkedProfileId → BrowserProfile.LinkedAccountId (legacy) → fallback folder.
    /// </summary>
    public async Task<ProfileResolution> ResolveProfileAsync(int accountId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        // 1. Profile linked directly on the account
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
        if (account?.LinkedProfileId is int pid)
        {
            var p = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid && x.Status == "active");
            if (p is not null)
            {
                _ = UpdateLastUsedAsync(p.Id);
                return new ProfileResolution(p.ProfilePath, p.Proxy);
            }
        }

        // 2. Legacy: profile that has LinkedAccountId pointing to this account
        var legacy = await db.Profiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.LinkedAccountId == accountId && p.Status == "active");
        if (legacy is not null)
        {
            _ = UpdateLastUsedAsync(legacy.Id);
            return new ProfileResolution(legacy.ProfilePath, legacy.Proxy);
        }

        // 3. Fallback folder
        var fallbackPath = Path.GetFullPath(Path.Combine("profiles", accountId.ToString()));
        Directory.CreateDirectory(fallbackPath);
        return new ProfileResolution(fallbackPath, null);
    }

    // ── Accounts ───────────────────────────────────────────────────────────

    public async Task<StoredAccount> CreateAccountAsync(string username, int? linkedProfileId = null, string? notes = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        var entity = new StoredAccount
        {
            Username        = username.Trim(),
            Platform        = "multi",          // determined by pages
            LinkedProfileId = linkedProfileId,
            Notes           = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        db.Accounts.Add(entity);
        await db.SaveChangesAsync();

        Log.Information("Created account '{Username}' (id={Id})", entity.Username, entity.Id);
        return entity;
    }

    public async Task<List<StoredAccount>> GetAllAccountsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        return await db.Accounts.AsNoTracking().OrderBy(a => a.Username).ToListAsync();
    }

    public async Task DeleteAccountAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.Accounts.FindAsync(id);
        if (entity is null) return;
        db.Accounts.Remove(entity);
        await db.SaveChangesAsync();
        Log.Information("Deleted account id={Id}", id);
    }

    public async Task SetProxyEnabledAsync(int profileId, bool enabled)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.Profiles.FindAsync(profileId);
        if (entity is null) return;
        entity.ProxyEnabled = enabled;
        await db.SaveChangesAsync();
    }

    public async Task SetAccountProfileAsync(int accountId, int? profileId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.Accounts.FindAsync(accountId);
        if (entity is null) return;
        entity.LinkedProfileId = profileId;
        await db.SaveChangesAsync();
    }

    // ── Pages (per account) ────────────────────────────────────────────────

    public async Task<ProfilePage> AddPageAsync(int accountId, string url, string? label = null, string language = "ru")
    {
        var platform = DetectPlatform(url);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        var entity = new ProfilePage
        {
            AccountId = accountId,
            Platform  = platform,
            Url       = url.Trim(),
            Label     = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            Language  = string.IsNullOrWhiteSpace(language) ? "ru" : language.Trim()
        };
        db.ProfilePages.Add(entity);
        await db.SaveChangesAsync();

        Log.Information("Added page '{Url}' (platform={Platform}) to account {AccountId}", url, platform, accountId);
        return entity;
    }

    public async Task<List<ProfilePage>> GetPagesForAccountAsync(int accountId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        return await db.ProfilePages
            .AsNoTracking()
            .Where(p => p.AccountId == accountId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task RemovePageAsync(int pageId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.ProfilePages.FindAsync(pageId);
        if (entity is null) return;
        db.ProfilePages.Remove(entity);
        await db.SaveChangesAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    public static string DetectPlatform(string url)
    {
        if (url.Contains("instagram.com", StringComparison.OrdinalIgnoreCase)) return "instagram";
        if (url.Contains("threads.net",   StringComparison.OrdinalIgnoreCase)) return "threads";
        if (url.Contains("threads.com",   StringComparison.OrdinalIgnoreCase)) return "threads";
        if (url.Contains("facebook.com",  StringComparison.OrdinalIgnoreCase)) return "facebook";
        if (url.Contains("tiktok.com",    StringComparison.OrdinalIgnoreCase)) return "tiktok";
        if (url.Contains("twitter.com",   StringComparison.OrdinalIgnoreCase)) return "twitter";
        if (url.Contains("x.com",         StringComparison.OrdinalIgnoreCase)) return "x";
        return "other";
    }

    public record ProfileResolution(string ProfilePath, string? Proxy);

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
