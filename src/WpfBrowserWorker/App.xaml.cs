using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.IO;
using System.Windows;
using WpfBrowserWorker.Browser;
using WpfBrowserWorker.Browser.Actions;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Services;
using WpfBrowserWorker.ViewModels;

namespace WpfBrowserWorker;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = LoadConfig();
        ConfigureSerilog(config);

        var services = new ServiceCollection();
        RegisterServices(services, config);
        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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

        // Generate WorkerId on first run
        if (string.IsNullOrEmpty(config.WorkerId))
        {
            config.WorkerId = $"worker-{Environment.MachineName.ToLower()}-{Guid.NewGuid().ToString()[..8]}";
            // TODO: persist back to appsettings.json
        }

        return config;
    }

    private static void ConfigureSerilog(WorkerConfig config)
    {
        Directory.CreateDirectory("logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(config.LogLevel))
            .WriteTo.File("logs/worker-.log", rollingInterval: Serilog.RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void RegisterServices(IServiceCollection services, WorkerConfig config)
    {
        // Config
        services.AddSingleton(config);

        // HTTP
        services.AddHttpClient<IApiClient, ApiClient>(client =>
        {
            client.BaseAddress = new Uri(config.BackendUrl.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("X-Worker-Key", config.ApiKey);
        });

        // Core services
        services.AddSingleton<WorkerStateService>();
        services.AddSingleton<IBrowserManager, BrowserManager>();
        services.AddSingleton<HumanBehaviorSimulator>();
        services.AddSingleton<FingerprintGenerator>();
        services.AddSingleton<RetryPolicy>();

        // Actions
        services.AddTransient<IAction, LikeAction>();
        services.AddTransient<IAction, CommentAction>();
        services.AddTransient<IAction, FollowAction>();
        services.AddTransient<IAction, UnfollowAction>();
        services.AddTransient<IAction, ViewStoryAction>();
        services.AddTransient<IAction, ScrollFeedAction>();
        services.AddTransient<IAction, AnalyzeProfileAction>();
        services.AddTransient<IAction, ScreenshotAction>();
        services.AddTransient<IAction, DirectMessageAction>();
        services.AddTransient<IAction, CustomSelectorAction>();

        // Worker orchestration
        services.AddSingleton<TaskExecutor>();
        services.AddSingleton<TaskPoller>();
        services.AddSingleton<HeartbeatService>();
        services.AddSingleton<TaskTimeoutWatcher>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<TaskListViewModel>();
        services.AddTransient<AccountsViewModel>();
        services.AddTransient<LogsViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Main window
        services.AddSingleton<MainWindow>();
    }
}
