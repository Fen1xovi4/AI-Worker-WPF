using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IApiClient _apiClient;

    [ObservableProperty] private string _backendUrl = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _workerId = string.Empty;
    [ObservableProperty] private int _pollIntervalMs;
    [ObservableProperty] private int _maxBrowsers;
    [ObservableProperty] private string _chromiumPath = string.Empty;
    [ObservableProperty] private string _screenshotsPath = string.Empty;
    [ObservableProperty] private bool _humanMode;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _testResult = string.Empty;
    [ObservableProperty] private bool _isTesting;

    public SettingsViewModel(WorkerConfig config, IApiClient apiClient)
    {
        _apiClient = apiClient;
        LoadFrom(config);
    }

    private void LoadFrom(WorkerConfig config)
    {
        BackendUrl = config.BackendUrl;
        ApiKey = config.ApiKey;
        WorkerId = config.WorkerId;
        PollIntervalMs = config.PollIntervalMs;
        MaxBrowsers = config.MaxBrowsers;
        ChromiumPath = config.ChromiumPath ?? string.Empty;
        ScreenshotsPath = config.ScreenshotsPath;
        HumanMode = config.HumanMode;
        AutoStart = config.AutoStart;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var config = new WorkerConfig
        {
            BackendUrl = BackendUrl,
            ApiKey = ApiKey,
            WorkerId = WorkerId,
            PollIntervalMs = PollIntervalMs,
            MaxBrowsers = MaxBrowsers,
            ChromiumPath = string.IsNullOrEmpty(ChromiumPath) ? null : ChromiumPath,
            ScreenshotsPath = ScreenshotsPath,
            HumanMode = HumanMode,
            AutoStart = AutoStart
        };

        var json = JsonSerializer.Serialize(new { Worker = config }, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText("appsettings.json", json);
        TestResult = "Settings saved.";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestResult = "Testing...";
        try
        {
            var heartbeat = new Models.HeartbeatRequest
            {
                WorkerId = WorkerId,
                Version = "1.0.0",
                Hostname = Environment.MachineName
            };
            var result = await _apiClient.SendHeartbeatAsync(heartbeat);
            TestResult = result.Status == "ok" ? "Connected!" : $"Unexpected response: {result.Status}";
        }
        catch (Exception ex)
        {
            TestResult = $"Failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(BackendUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        ApiKey.StartsWith("wk_");
}
