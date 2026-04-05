using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.IO;
using System.Windows;
using WpfBrowserWorker.Api;
using WpfBrowserWorker.Browser;
using WpfBrowserWorker.Browser.Actions;
using WpfBrowserWorker.Data;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Services;
using WpfBrowserWorker.ViewModels;

namespace WpfBrowserWorker;

public partial class App : Application
{
    private WebApplication? _webApp;
    private CancellationTokenSource? _apiCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = LoadConfig();
        ConfigureSerilog(config);

        _webApp = BuildWebApp(config);

        // Запускаем Kestrel в фоне — не блокирует UI
        _apiCts = new CancellationTokenSource();
        _webApp.Urls.Add($"http://0.0.0.0:{config.ApiPort}");
        Task.Run(() => _webApp.RunAsync(_apiCts.Token));

        Log.Information("Kestrel started on port {Port}", config.ApiPort);

        // Start Telegram bots in background
        var telegramListener = _webApp.Services.GetRequiredService<TelegramListenerService>();
        _ = Task.Run(() => telegramListener.StartAllAsync());

        // Start local task scheduler
        _webApp.Services.GetRequiredService<SchedulerService>().Start();

        // Wire Serilog → LogsViewModel
        var logsVm = _webApp.Services.GetRequiredService<LogsViewModel>();
        InMemoryLogSink.Instance.Attach(logsVm.AddEntry);

        var mainWindow = _webApp.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _webApp?.Services.GetService<TelegramListenerService>()?.StopAll();
        _webApp?.Services.GetService<SchedulerService>()?.Stop();
        (_webApp?.Services.GetService<PublishOrchestrator>())?.DisposeAsync().AsTask().Wait();
        _apiCts?.Cancel();
        _webApp?.StopAsync(TimeSpan.FromSeconds(3)).Wait();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static WorkerConfig LoadConfig()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var config = configuration.GetSection("Worker").Get<WorkerConfig>() ?? new WorkerConfig();

        if (string.IsNullOrEmpty(config.WorkerId))
            config.WorkerId = $"worker-{Environment.MachineName.ToLower()}-{Guid.NewGuid().ToString()[..8]}";

        return config;
    }

    internal static AiConfig LoadAiConfig()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        return configuration.GetSection("Ai").Get<AiConfig>() ?? new AiConfig();
    }

    private static void ConfigureSerilog(WorkerConfig config)
    {
        Directory.CreateDirectory("logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(config.LogLevel))
            .WriteTo.File("logs/worker-.log",
                rollingInterval: Serilog.RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(InMemoryLogSink.Instance)
            .CreateLogger();
    }

    private static WebApplication BuildWebApp(WorkerConfig config)
    {
        var builder = WebApplication.CreateBuilder();

        // Подавляем лишние логи ASP.NET Core в консоль
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();

        // ── Services ─────────────────────────────────────────────

        builder.Services.AddSingleton(config);

        // AI
        var aiConfig = LoadAiConfig();
        builder.Services.AddSingleton(aiConfig);
        builder.Services.AddSingleton<AiContentService>();

        // SQLite
        builder.Services.AddDbContext<WorkerDbContext>(opt =>
            opt.UseSqlite($"Data Source={config.DatabasePath}"));

        // Core services
        builder.Services.AddSingleton<WorkerStateService>();
        builder.Services.AddSingleton<TaskQueue>();
        builder.Services.AddSingleton<ProfileService>();
        builder.Services.AddSingleton<TelegramBotService>();
        builder.Services.AddSingleton<PublishOrchestrator>();
        builder.Services.AddSingleton<TelegramListenerService>();
        builder.Services.AddSingleton<LocalTaskService>();
        builder.Services.AddSingleton<SchedulerService>();
        builder.Services.AddSingleton<IBrowserManager, BrowserManager>();
        builder.Services.AddSingleton<HumanBehaviorSimulator>();
        builder.Services.AddSingleton<FingerprintGenerator>();
        builder.Services.AddSingleton<RetryPolicy>();

        // Actions
        builder.Services.AddTransient<IAction, LikeAction>();
        builder.Services.AddTransient<IAction, CommentAction>();
        builder.Services.AddTransient<IAction, FollowAction>();
        builder.Services.AddTransient<IAction, UnfollowAction>();
        builder.Services.AddTransient<IAction, ViewStoryAction>();
        builder.Services.AddTransient<IAction, ScrollFeedAction>();
        builder.Services.AddTransient<IAction, AnalyzeProfileAction>();
        builder.Services.AddTransient<IAction, ScreenshotAction>();
        builder.Services.AddTransient<IAction, DirectMessageAction>();
        builder.Services.AddTransient<IAction, CustomSelectorAction>();

        // Worker orchestration
        builder.Services.AddSingleton<TaskExecutor>();
        builder.Services.AddSingleton<TaskDispatcher>();
        builder.Services.AddSingleton<TaskTimeoutWatcher>();

        // API key filter
        builder.Services.AddTransient<ApiKeyFilter>();

        // ViewModels
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<TaskListViewModel>(sp =>
            new TaskListViewModel(
                sp.GetRequiredService<WorkerStateService>(),
                sp.GetRequiredService<LocalTaskService>(),
                sp.GetRequiredService<ProfileService>()));
        builder.Services.AddTransient<AccountsViewModel>(sp =>
            new AccountsViewModel(
                sp.GetRequiredService<ProfileService>(),
                sp.GetRequiredService<TelegramBotService>(),
                sp.GetRequiredService<TelegramListenerService>()));
        builder.Services.AddSingleton<LogsViewModel>();
        builder.Services.AddTransient<ProfilesViewModel>();
        builder.Services.AddTransient<SettingsViewModel>(sp =>
            new SettingsViewModel(
                sp.GetRequiredService<WorkerConfig>(),
                sp.GetRequiredService<AiConfig>(),
                sp.GetRequiredService<ProfilesViewModel>()));

        builder.Services.AddSingleton<MainWindow>();

        // ── Build ─────────────────────────────────────────────────

        var app = builder.Build();

        // Migrate / create DB on startup
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
            db.Database.EnsureCreated();

            // EnsureCreated only creates tables when DB is new.
            // For existing DBs we add missing tables manually (idempotent).
            // NOTE: Microsoft.Data.Sqlite does not support multiple statements per ExecuteSqlRaw call.
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "BrowserProfiles" (
                    "Id"              INTEGER NOT NULL CONSTRAINT "PK_BrowserProfiles" PRIMARY KEY AUTOINCREMENT,
                    "Name"            TEXT    NOT NULL DEFAULT '',
                    "ProfilePath"     TEXT    NOT NULL DEFAULT '',
                    "Proxy"           TEXT,
                    "Status"          TEXT    NOT NULL DEFAULT 'active',
                    "LinkedAccountId" INTEGER,
                    "Notes"           TEXT,
                    "CreatedAt"       TEXT    NOT NULL DEFAULT '',
                    "LastUsedAt"      TEXT
                )
                """);
            db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_BrowserProfiles_Name" ON "BrowserProfiles" ("Name") """);
            db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_BrowserProfiles_LinkedAccountId" ON "BrowserProfiles" ("LinkedAccountId") """);
            db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_BrowserProfiles_Status" ON "BrowserProfiles" ("Status") """);

            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "ProfilePages" (
                    "Id"        INTEGER NOT NULL CONSTRAINT "PK_ProfilePages" PRIMARY KEY AUTOINCREMENT,
                    "AccountId" INTEGER NOT NULL,
                    "Platform"  TEXT    NOT NULL DEFAULT '',
                    "Url"       TEXT    NOT NULL DEFAULT '',
                    "Label"     TEXT,
                    "CreatedAt" TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT "FK_ProfilePages_Accounts_AccountId"
                        FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE
                )
                """);
            db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ProfilePages_AccountId" ON "ProfilePages" ("AccountId") """);
            // Add LinkedProfileId column to Accounts if missing (idempotent via try-catch)
            try { db.Database.ExecuteSqlRaw("""ALTER TABLE "Accounts" ADD COLUMN "LinkedProfileId" INTEGER NULL"""); } catch { }
            try { db.Database.ExecuteSqlRaw("""ALTER TABLE "BrowserProfiles" ADD COLUMN "ProxyEnabled" INTEGER NOT NULL DEFAULT 1"""); } catch { }
            try { db.Database.ExecuteSqlRaw("""ALTER TABLE "ProfilePages" ADD COLUMN "Language" TEXT NOT NULL DEFAULT 'ru'"""); } catch { }

            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "TelegramBotConfigs" (
                    "Id"          INTEGER NOT NULL CONSTRAINT "PK_TelegramBotConfigs" PRIMARY KEY AUTOINCREMENT,
                    "AccountId"   INTEGER NOT NULL,
                    "BotToken"    TEXT    NOT NULL DEFAULT '',
                    "BotUsername" TEXT,
                    "IsActive"    INTEGER NOT NULL DEFAULT 1,
                    "LastChatId"  INTEGER,
                    "CreatedAt"   TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT "FK_TelegramBotConfigs_Accounts_AccountId"
                        FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE
                )
                """);
            db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TelegramBotConfigs_AccountId" ON "TelegramBotConfigs" ("AccountId") """);

            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "PublishLogs" (
                    "Id"           INTEGER NOT NULL CONSTRAINT "PK_PublishLogs" PRIMARY KEY AUTOINCREMENT,
                    "AccountId"    INTEGER NOT NULL,
                    "Platform"     TEXT    NOT NULL DEFAULT '',
                    "Status"       TEXT    NOT NULL DEFAULT 'ok',
                    "PostText"     TEXT,
                    "ErrorMessage" TEXT,
                    "PublishedAt"  TEXT    NOT NULL DEFAULT ''
                )
                """);
            db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PublishLogs_AccountId" ON "PublishLogs" ("AccountId") """);

            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "LocalScheduledTasks" (
                    "Id"          INTEGER NOT NULL CONSTRAINT "PK_LocalScheduledTasks" PRIMARY KEY AUTOINCREMENT,
                    "Name"        TEXT    NOT NULL DEFAULT '',
                    "TaskType"    TEXT    NOT NULL DEFAULT 'like',
                    "Platform"    TEXT    NOT NULL DEFAULT 'instagram',
                    "AccountId"   INTEGER,
                    "TargetUrl"   TEXT,
                    "Count"       INTEGER NOT NULL DEFAULT 50,
                    "RepeatMode"  TEXT    NOT NULL DEFAULT 'once',
                    "DaysJson"    TEXT    NOT NULL DEFAULT '[]',
                    "TimeOfDay"   TEXT    NOT NULL DEFAULT '09:00',
                    "IsActive"    INTEGER NOT NULL DEFAULT 1,
                    "LastRunAt"   TEXT,
                    "NextRunAt"   TEXT,
                    "CreatedAt"   TEXT    NOT NULL DEFAULT ''
                )
                """);
        }

        app.MapWorkerEndpoints();

        return app;
    }
}
