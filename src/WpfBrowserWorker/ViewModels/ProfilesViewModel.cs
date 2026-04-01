using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using WpfBrowserWorker.Data.Entities;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileService _profileService;
    private readonly WorkerConfig _config;

    public ObservableCollection<BrowserProfile> Profiles { get; } = new();

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private string _newProfileName = string.Empty;

    [ObservableProperty] private string _newProfileProxy = string.Empty;
    [ObservableProperty] private string _newProfileNotes = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private bool _isBusy;

    [ObservableProperty] private BrowserProfile? _selectedProfile;

    public ProfilesViewModel(ProfileService profileService, WorkerConfig config)
    {
        _profileService = profileService;
        _config = config;
    }

    public async Task LoadAsync()
    {
        var items = await _profileService.GetAllAsync();
        Profiles.Clear();
        foreach (var p in items)
            Profiles.Add(p);
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateProfileAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var profile = await _profileService.CreateProfileAsync(
                NewProfileName.Trim(),
                string.IsNullOrWhiteSpace(NewProfileProxy) ? null : NewProfileProxy.Trim(),
                string.IsNullOrWhiteSpace(NewProfileNotes) ? null : NewProfileNotes.Trim());

            Profiles.Add(profile);
            NewProfileName = string.Empty;
            NewProfileProxy = string.Empty;
            NewProfileNotes = string.Empty;
            StatusMessage = $"Profile '{profile.Name}' created at {profile.ProfilePath}";
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.InnerException?.Message
                     ?? ex.InnerException?.Message
                     ?? ex.Message;
            StatusMessage = $"Error: {inner}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(NewProfileName) && !IsBusy;

    [RelayCommand]
    private async Task DeleteProfileAsync(BrowserProfile? profile)
    {
        if (profile is null) return;
        await _profileService.DeleteAsync(profile.Id);
        Profiles.Remove(profile);
        SelectedProfile = null;
        StatusMessage = $"Profile '{profile.Name}' removed (folder kept on disk).";
    }

    // Holds the relay and ChromeDriver for the active test launch
    private HttpConnectRelay? _activeRelay;
    private IWebDriver? _testDriver;

    [RelayCommand]
    private async Task TestLaunchAsync(BrowserProfile? profile)
    {
        if (profile is null) return;

        // Close any previous test browser
        try { _testDriver?.Quit(); } catch { }
        try { _testDriver?.Dispose(); } catch { }
        _testDriver = null;
        _activeRelay?.Dispose();
        _activeRelay = null;

        StatusMessage = "Starting test browser…";

        try
        {
            var options = new ChromeOptions();
            options.AddArgument("--no-first-run");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--disable-extensions");

            options.AddArgument($"--user-data-dir={profile.ProfilePath}");

            if (!string.IsNullOrWhiteSpace(profile.Proxy) && profile.ProxyEnabled)
            {
                var (host, port, user, pass) = ParseProxyParts(profile.Proxy.Trim());
                if (user is not null)
                {
                    var relay = new HttpConnectRelay(host, port, user, pass);
                    _activeRelay = relay;
                    options.AddArgument($"--proxy-server=http://127.0.0.1:{relay.LocalPort}");
                }
                else
                {
                    options.AddArgument($"--proxy-server=socks5://{host}:{port}");
                }
            }

            if (!string.IsNullOrEmpty(_config.ChromiumPath))
                options.BinaryLocation = _config.ChromiumPath;

            var driverDir = string.IsNullOrEmpty(_config.ChromiumPath)
                ? null
                : Path.GetDirectoryName(_config.ChromiumPath);

            var driver = driverDir is not null
                ? new ChromeDriver(driverDir, options)
                : new ChromeDriver(options);
            _testDriver = driver;

            driver.Navigate().GoToUrl("https://ipinfo.io/ip");
            await Task.Delay(3000);

            var ip = driver.FindElement(By.TagName("body")).Text.Trim();
            StatusMessage = string.IsNullOrWhiteSpace(profile.Proxy)
                ? $"Browser IP (no proxy): {ip}"
                : $"Browser IP via proxy: {ip}  (expected: {profile.Proxy.Split(':')[0]})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Test launch error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestProxyAsync(BrowserProfile? profile)
    {
        if (profile is null) return;
        if (string.IsNullOrWhiteSpace(profile.Proxy))
        {
            StatusMessage = "No proxy set for this profile.";
            return;
        }

        StatusMessage = "Checking proxy IP...";
        try
        {
            var (host, port, user, pass) = ParseProxyParts(profile.Proxy.Trim());

            var handler = new System.Net.Http.SocketsHttpHandler
            {
                Proxy = new System.Net.WebProxy($"socks5://{host}:{port}")
                {
                    Credentials = user is not null
                        ? new System.Net.NetworkCredential(user, pass)
                        : null
                },
                UseProxy = true
            };

            using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var ip = await http.GetStringAsync("https://ipinfo.io/ip");
            StatusMessage = $"Proxy OK — IP: {ip.Trim()}";
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            StatusMessage = $"Proxy error: {msg}";
        }
    }

    private static (string host, int port, string? user, string? pass) ParseProxyParts(string raw)
    {
        var s = raw;
        if (s.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase)) s = s["socks5://".Length..];
        else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = s["http://".Length..];

        var parts = s.Split(':');
        var host = parts[0];
        var port = parts.Length >= 2 && int.TryParse(parts[1], out var p) ? p : 1080;
        var user = parts.Length >= 3 ? parts[2] : null;
        var pass = parts.Length >= 4 ? parts[3] : null;
        return (host, port, user, pass);
    }

    private string? FindChrome()
    {
        // 1. Explicit path from config
        if (!string.IsNullOrEmpty(_config.ChromiumPath) && File.Exists(_config.ChromiumPath))
            return _config.ChromiumPath;

        // 2. Common install locations
        string[] candidates =
        [
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\Application\chrome.exe"),
            @"C:\Program Files\Chromium\Application\chrome.exe",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    [RelayCommand]
    private async Task ToggleProxyAsync(BrowserProfile? profile)
    {
        if (profile is null) return;
        profile.ProxyEnabled = !profile.ProxyEnabled;
        await _profileService.SetProxyEnabledAsync(profile.Id, profile.ProxyEnabled);
        OnPropertyChanged(nameof(profile.ProxyEnabled));
        StatusMessage = profile.ProxyEnabled
            ? $"Proxy enabled for '{profile.Name}'"
            : $"Proxy disabled for '{profile.Name}'";
    }

    [RelayCommand]
    private static void OpenProfileFolder(BrowserProfile? profile)
    {
        if (profile is null) return;
        if (Directory.Exists(profile.ProfilePath))
            System.Diagnostics.Process.Start("explorer.exe", profile.ProfilePath);
        else
            System.Diagnostics.Process.Start("explorer.exe",
                Path.GetDirectoryName(profile.ProfilePath) ?? profile.ProfilePath);
    }
}
