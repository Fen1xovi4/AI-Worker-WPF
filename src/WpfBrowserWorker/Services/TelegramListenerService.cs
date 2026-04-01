using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WpfBrowserWorker.Data.Entities;

namespace WpfBrowserWorker.Services;

/// <summary>
/// Manages one TelegramBotClient per active account bot.
/// On message: parses platforms → generates AI content → replies.
/// Phase 3 will extend this to trigger browser publishing.
/// </summary>
public class TelegramListenerService
{
    // accountId → (client, cts, configId)
    private readonly ConcurrentDictionary<int, (TelegramBotClient Client, CancellationTokenSource Cts, int ConfigId)> _bots = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiContentService     _aiService;
    private readonly TelegramBotService   _botService;
    private readonly PublishOrchestrator  _publisher;

    public TelegramListenerService(
        IServiceScopeFactory scopeFactory,
        AiContentService     aiService,
        TelegramBotService   botService,
        PublishOrchestrator  publisher)
    {
        _scopeFactory = scopeFactory;
        _aiService    = aiService;
        _botService   = botService;
        _publisher    = publisher;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Start all active bots from DB (called on app startup).</summary>
    public async Task StartAllAsync()
    {
        var configs = await _botService.GetAllActiveAsync();
        foreach (var cfg in configs)
            await StartBotAsync(cfg);
    }

    /// <summary>Start a single bot. Replaces existing instance if token changed.</summary>
    public async Task<string> StartBotAsync(TelegramBotConfig config)
    {
        StopBot(config.AccountId);

        try
        {
            var client = new TelegramBotClient(config.BotToken);
            var me     = await client.GetMe();

            var cts = new CancellationTokenSource();
            var opts = new ReceiverOptions { AllowedUpdates = [UpdateType.Message] };

            client.StartReceiving(
                updateHandler:      (bot, upd, ct) => HandleUpdateAsync(bot, upd, config.AccountId, ct),
                errorHandler:       HandlePollingErrorAsync,
                receiverOptions:    opts,
                cancellationToken:  cts.Token);

            _bots[config.AccountId] = (client, cts, config.Id);
            Log.Information("Telegram bot @{Username} started for account {AccountId}", me.Username, config.AccountId);
            return $"@{me.Username}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start Telegram bot for account {AccountId}", config.AccountId);
            throw;
        }
    }

    public void StopBot(int accountId)
    {
        if (_bots.TryRemove(accountId, out var entry))
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
            Log.Information("Telegram bot stopped for account {AccountId}", accountId);
        }
    }

    public bool IsRunning(int accountId) => _bots.ContainsKey(accountId);

    public void StopAll()
    {
        foreach (var key in _bots.Keys.ToList())
            StopBot(key);
    }

    // ── Message handling ──────────────────────────────────────────────────────

    private async Task HandleUpdateAsync(
        ITelegramBotClient bot, Update update, int accountId, CancellationToken ct)
    {
        if (update.Message is not { } msg) return;

        var chatId = msg.Chat.Id;
        var text   = (msg.Text ?? msg.Caption ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text) && msg.Photo is null)
        {
            await bot.SendMessage(chatId,
                "Отправьте фото с подписью или текстовое задание.",
                cancellationToken: ct);
            return;
        }

        Log.Information("Telegram bot got message from chat {ChatId} for account {AccountId}: '{Text}'",
            chatId, accountId, text.Length > 60 ? text[..60] + "…" : text);

        _ = _botService.UpdateChatIdAsync(accountId, chatId);

        // Download photo (if any)
        byte[]? imageBytes = null;
        if (msg.Photo is { Length: > 0 } photos)
        {
            try
            {
                var largest = photos.MaxBy(p => p.FileSize ?? 0)!;
                var file    = await bot.GetFile(largest.FileId, ct);
                using var ms = new System.IO.MemoryStream();
                await bot.DownloadFile(file.FilePath!, ms, ct);
                imageBytes = ms.ToArray();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to download Telegram photo");
            }
        }

        // Get account pages
        var pages = await GetTargetPagesAsync(accountId, text);
        if (pages.Count == 0)
        {
            await bot.SendMessage(chatId,
                "У аккаунта нет привязанных страниц. Добавьте их в разделе Accounts.",
                cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId,
            $"Генерирую контент для {pages.Count} площадок…",
            cancellationToken: ct);

        // ── Generate texts once, store per page ───────────────────────────────
        var generated = new List<(Data.Entities.ProfilePage Page, string? Text, string? Error)>();
        foreach (var page in pages)
        {
            try
            {
                var post = await _aiService.GeneratePostAsync(
                    page.Platform, page.Language, text, imageBytes);
                generated.Add((page, post, null));
            }
            catch (Exception ex)
            {
                generated.Add((page, null, ex.Message));
                Log.Warning(ex, "AI generation failed for platform {Platform}", page.Platform);
            }
        }

        // ── Send preview to Telegram ───────────────────────────────────────────
        var previewSb = new StringBuilder();
        foreach (var (page, postText, error) in generated)
        {
            var emoji = PlatformEmoji(page.Platform);
            if (error is not null)
                previewSb.AppendLine($"❌ {page.Platform}: {error}");
            else
            {
                previewSb.AppendLine($"{emoji} {CapFirst(page.Platform)}:");
                previewSb.AppendLine(postText);
            }
            previewSb.AppendLine();
        }

        var preview = previewSb.ToString().Trim();
        if (preview.Length > 4096) preview = preview[..4090] + "\n…";
        await bot.SendMessage(chatId, preview, cancellationToken: ct);

        // ── Publish sequentially, one platform at a time ──────────────────────
        await bot.SendMessage(chatId, "Публикую по очереди…", cancellationToken: ct);

        var publishSb = new StringBuilder();
        foreach (var (page, postText, genError) in generated)
        {
            if (genError is not null || postText is null)
            {
                publishSb.AppendLine($"❌ {CapFirst(page.Platform)}: пропущено (AI ошибка)");
                continue;
            }

            await bot.SendMessage(chatId,
                $"{PlatformEmoji(page.Platform)} Публикую в {CapFirst(page.Platform)}…",
                cancellationToken: ct);

            var pub = await _publisher.PublishAsync(
                accountId, page.Platform, postText, imageBytes, ct);

            var mark   = pub.Success ? "✅" : "❌";
            var detail = pub.Success ? "опубликовано" : pub.Error ?? "ошибка";
            publishSb.AppendLine($"{mark} {CapFirst(page.Platform)}: {detail}");

            // Pause between platforms — look natural, avoid rapid sequential posts
            if (generated.IndexOf((page, postText, genError)) < generated.Count - 1)
                await Task.Delay(Random.Shared.Next(15_000, 35_000), ct);
        }

        var publishReport = publishSb.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(publishReport))
            await bot.SendMessage(chatId, publishReport, cancellationToken: ct);

        // Close Chrome after all platforms are done — prevents locked profiles on next request
        _publisher.CloseDriver(accountId);
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception ex, HandleErrorSource src, CancellationToken ct)
    {
        Log.Warning(ex, "Telegram polling error (source={Source})", src);
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<Data.Entities.ProfilePage>> GetTargetPagesAsync(int accountId, string text)
    {
        using var scope = _scopeFactory.CreateScope();
        var profileService = scope.ServiceProvider.GetRequiredService<ProfileService>();
        var allPages = await profileService.GetPagesForAccountAsync(accountId);

        var requested = ParsePlatforms(text);
        return requested.Count == 0
            ? allPages                                               // no platform specified → all
            : allPages.Where(p => requested.Contains(p.Platform)).ToList();
    }

    private static List<string> ParsePlatforms(string text)
    {
        var lower = text.ToLower();

        // "везде" / "все площадки" / "all" → empty = all
        if (lower.Contains("везде") || lower.Contains("все площадки") ||
            lower.Contains("all")   || lower.Contains("всё"))
            return [];

        var result = new List<string>();

        if (lower.Contains("инстаграм") || lower.Contains("instagram")) result.Add("instagram");
        if (lower.Contains("треадс")    || lower.Contains("threads"))   result.Add("threads");
        if (lower.Contains("тикток")    || lower.Contains("tiktok"))    result.Add("tiktok");
        if (lower.Contains("фейсбук")   || lower.Contains("facebook"))  result.Add("facebook");
        if (lower.Contains("твиттер")   || lower.Contains("twitter"))   result.Add("twitter");
        if (lower.Contains(" x ")       || lower.Contains("x.com"))     result.Add("x");

        return result;
    }

    private static string PlatformEmoji(string platform) => platform.ToLower() switch
    {
        "instagram" => "📸",
        "threads"   => "🧵",
        "tiktok"    => "🎵",
        "facebook"  => "📘",
        "twitter"   => "🐦",
        "x"         => "✖️",
        _           => "📄"
    };

    private static string CapFirst(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
