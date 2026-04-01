using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WpfBrowserWorker.Data;
using WpfBrowserWorker.Data.Entities;

namespace WpfBrowserWorker.Services;

/// <summary>
/// CRUD operations for TelegramBotConfig.
/// Each account can have at most one Telegram bot.
/// </summary>
public class TelegramBotService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TelegramBotService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<TelegramBotConfig?> GetByAccountAsync(int accountId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        return await db.TelegramBots.AsNoTracking()
            .FirstOrDefaultAsync(b => b.AccountId == accountId);
    }

    public async Task<List<TelegramBotConfig>> GetAllActiveAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        return await db.TelegramBots.AsNoTracking()
            .Where(b => b.IsActive)
            .ToListAsync();
    }

    /// <summary>
    /// Insert or update the bot token for an account.
    /// </summary>
    public async Task<TelegramBotConfig> SaveAsync(int accountId, string token, string? username = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        var existing = await db.TelegramBots.FirstOrDefaultAsync(b => b.AccountId == accountId);
        if (existing is not null)
        {
            existing.BotToken   = token.Trim();
            existing.BotUsername = username;
            existing.IsActive   = true;
        }
        else
        {
            existing = new TelegramBotConfig
            {
                AccountId   = accountId,
                BotToken    = token.Trim(),
                BotUsername = username,
                IsActive    = true
            };
            db.TelegramBots.Add(existing);
        }

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task RemoveAsync(int accountId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.TelegramBots.FirstOrDefaultAsync(b => b.AccountId == accountId);
        if (entity is null) return;
        db.TelegramBots.Remove(entity);
        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int accountId, bool active)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.TelegramBots.FirstOrDefaultAsync(b => b.AccountId == accountId);
        if (entity is null) return;
        entity.IsActive = active;
        await db.SaveChangesAsync();
    }

    public async Task UpdateChatIdAsync(int accountId, long chatId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.TelegramBots.FirstOrDefaultAsync(b => b.AccountId == accountId);
        if (entity is null) return;
        entity.LastChatId = chatId;
        await db.SaveChangesAsync();
    }
}
