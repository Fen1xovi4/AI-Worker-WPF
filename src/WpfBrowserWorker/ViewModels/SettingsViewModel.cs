using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _workerId = string.Empty;
    [ObservableProperty] private int _apiPort;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private int _maxBrowsers;
    [ObservableProperty] private string _chromiumPath = string.Empty;
    [ObservableProperty] private string _screenshotsPath = string.Empty;
    [ObservableProperty] private string _databasePath = string.Empty;
    [ObservableProperty] private bool _humanMode;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _testResult = string.Empty;
    [ObservableProperty] private bool _isTesting;

    // ── AI settings ────────────────────────────────────────────────────────
    [ObservableProperty] private string _aiProvider    = "openai";
    [ObservableProperty] private string _openAiKey     = string.Empty;
    [ObservableProperty] private string _openAiModel   = "gpt-4o-mini";
    [ObservableProperty] private string _deepSeekKey   = string.Empty;
    [ObservableProperty] private string _deepSeekModel = "deepseek-chat";

    public ProfilesViewModel ProfilesVm { get; }

    public SettingsViewModel(WorkerConfig config, AiConfig aiConfig, ProfilesViewModel profilesVm)
    {
        ProfilesVm = profilesVm;
        LoadFrom(config);
        LoadAi(aiConfig);
        _ = profilesVm.LoadAsync();
    }

    private void LoadAi(AiConfig ai)
    {
        AiProvider    = ai.Provider;
        OpenAiKey     = ai.OpenAiKey;
        OpenAiModel   = ai.OpenAiModel;
        DeepSeekKey   = ai.DeepSeekKey;
        DeepSeekModel = ai.DeepSeekModel;
    }

    private void LoadFrom(WorkerConfig config)
    {
        WorkerId = config.WorkerId;
        ApiPort = config.ApiPort;
        ApiKey = config.ApiKey;
        MaxBrowsers = config.MaxBrowsers;
        ChromiumPath = config.ChromiumPath ?? string.Empty;
        ScreenshotsPath = config.ScreenshotsPath;
        DatabasePath = config.DatabasePath;
        HumanMode = config.HumanMode;
        AutoStart = config.AutoStart;
    }

    [RelayCommand]
    private void Save()
    {
        var config = new WorkerConfig
        {
            WorkerId        = WorkerId,
            ApiPort         = ApiPort,
            ApiKey          = ApiKey,
            MaxBrowsers     = MaxBrowsers,
            ChromiumPath    = string.IsNullOrEmpty(ChromiumPath) ? null : ChromiumPath,
            ScreenshotsPath = ScreenshotsPath,
            DatabasePath    = DatabasePath,
            HumanMode       = HumanMode,
            AutoStart       = AutoStart
        };

        var ai = new AiConfig
        {
            Provider      = AiProvider,
            OpenAiKey     = OpenAiKey,
            OpenAiModel   = OpenAiModel,
            DeepSeekKey   = DeepSeekKey,
            DeepSeekModel = DeepSeekModel
        };

        var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var json = JsonSerializer.Serialize(new { Worker = config, Ai = ai }, opts);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), json);

        TestResult = "Saved. Restart to apply changes.";
    }

    [RelayCommand]
    private async Task TestApiAsync()
    {
        IsTesting = true;
        TestResult = "Checking...";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            if (!string.IsNullOrEmpty(ApiKey))
                http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

            var resp = await http.GetStringAsync($"http://localhost:{ApiPort}/api/status");
            TestResult = $"API OK — {resp[..Math.Min(80, resp.Length)]}";
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
}
