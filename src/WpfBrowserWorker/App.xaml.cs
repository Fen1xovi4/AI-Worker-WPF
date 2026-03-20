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

        var mainWindow = _webApp.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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

    private static void ConfigureSerilog(WorkerConfig config)
    {
        Directory.CreateDirectory("logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(config.LogLevel))
            .WriteTo.File("logs/worker-.log",
                rollingInterval: Serilog.RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
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

        // SQLite
        builder.Services.AddDbContext<WorkerDbContext>(opt =>
            opt.UseSqlite($"Data Source={config.DatabasePath}"));

        // Core services
        builder.Services.AddSingleton<WorkerStateService>();
        builder.Services.AddSingleton<TaskQueue>();
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
        builder.Services.AddTransient<TaskListViewModel>();
        builder.Services.AddTransient<AccountsViewModel>();
        builder.Services.AddTransient<LogsViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        builder.Services.AddSingleton<MainWindow>();

        // ── Build ─────────────────────────────────────────────────

        var app = builder.Build();

        // Migrate / create DB on startup
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
            db.Database.EnsureCreated();
        }

        app.MapWorkerEndpoints();

        return app;
    }
}
