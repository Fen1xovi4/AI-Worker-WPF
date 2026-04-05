using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Serilog;
using WpfBrowserWorker.Browser.Publishing;
using WpfBrowserWorker.Data;
using WpfBrowserWorker.Data.Entities;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

/// <summary>
/// Manages dedicated Chrome instances for publishing.
/// One browser per account, kept open between posts.
/// </summary>
public class PublishOrchestrator : IAsyncDisposable
{
    private readonly ProfileService          _profileService;
    private readonly WorkerConfig            _config;
    private readonly HumanBehaviorSimulator  _human;
    private readonly IServiceScopeFactory    _scopeFactory;

    // accountId → driver kept alive between publishes
    private readonly ConcurrentDictionary<int, IWebDriver> _browsers = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PublishOrchestrator(
        ProfileService         profileService,
        WorkerConfig           config,
        HumanBehaviorSimulator human,
        IServiceScopeFactory   scopeFactory)
    {
        _profileService = profileService;
        _config         = config;
        _human          = human;
        _scopeFactory   = scopeFactory;

        KillStaleChromedrivers();
    }

    /// <summary>
    /// Kills any chromedriver.exe processes left over from a previous session.
    /// Called once on startup so stale locks don't block new Chrome instances.
    /// </summary>
    private static void KillStaleChromedrivers()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("chromedriver"))
            {
                try { proc.Kill(); proc.WaitForExit(2000); }
                catch { }
                finally { proc.Dispose(); }
            }
            Log.Debug("PublishOrchestrator: stale chromedriver processes cleaned up");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "PublishOrchestrator: error cleaning up stale chromedrivers");
        }
    }

    /// <summary>
    /// Publish a post to a specific platform for a given account.
    /// Opens (or reuses) the Chrome profile that is linked to the account.
    /// </summary>
    public async Task<PublishResult> PublishAsync(
        int     accountId,
        string  platform,
        string  postText,
        byte[]? imageBytes,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        IWebDriver driver;
        try
        {
            driver = await GetOrCreateDriverAsync(accountId, ct);
        }
        finally
        {
            _lock.Release();
        }

        PublishResult result;
        try
        {
            result = platform.ToLower() switch
            {
                "instagram"         => await new InstagramPublisher(_human).PublishAsync(driver, postText, imageBytes, ct),
                "threads"           => await new ThreadsPublisher(_human)  .PublishAsync(driver, postText, imageBytes, ct),
                _                   => PublishResult.Fail($"Auto-publishing not supported for platform '{platform}'")
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "PublishOrchestrator unhandled error for {Platform}", platform);
            result = PublishResult.Fail(ex.Message);
            // Browser may be in bad state — remove so it's recreated next time
            RemoveDriver(accountId);
        }

        await LogResultAsync(accountId, platform, postText, result);
        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<IWebDriver> GetOrCreateDriverAsync(int accountId, CancellationToken ct)
    {
        if (_browsers.TryGetValue(accountId, out var existing))
        {
            // Verify it's still alive
            try
            {
                _ = existing.CurrentWindowHandle;
                return existing;
            }
            catch
            {
                RemoveDriver(accountId);
            }
        }

        var resolution = await _profileService.ResolveProfileAsync(accountId);

        // Kill any Chrome processes still holding this profile, then retry once on crash.
        IWebDriver driver;
        try
        {
            KillChromeForProfile(resolution.ProfilePath);
            driver = CreateDriver(resolution.ProfilePath, resolution.Proxy);
        }
        catch (Exception ex) when (ex.Message.Contains("DevToolsActivePort") || ex.Message.Contains("crashed"))
        {
            Log.Warning("Chrome crashed on startup [account #{AccountId}] — retrying after cleanup", accountId);
            await Task.Delay(3000, ct);
            KillChromeForProfile(resolution.ProfilePath);
            ReleaseProfileLock(resolution.ProfilePath);
            driver = CreateDriver(resolution.ProfilePath, resolution.Proxy);
        }

        _browsers[accountId] = driver;
        Log.Information("Browser opened [account #{AccountId}]", accountId);
        return driver;
    }

    /// <summary>
    /// Kills chrome.exe processes whose command line contains the given profile path.
    /// Leaves other Chrome windows (personal browsing) untouched.
    /// </summary>
    private static void KillChromeForProfile(string profilePath)
    {
        var fullPath = Path.GetFullPath(profilePath).TrimEnd('\\', '/').ToLowerInvariant();
        try
        {
            foreach (var proc in Process.GetProcessesByName("chrome"))
            {
                try
                {
                    // Read the command line via WMI to check for our profile path
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {proc.Id}");
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        var cmd = (obj["CommandLine"] as string ?? "").ToLowerInvariant();
                        if (cmd.Contains(fullPath))
                        {
                            try { proc.Kill(); proc.WaitForExit(2000); }
                            catch { }
                            Log.Debug("Killed chrome.exe PID {Pid} holding profile {Path}", proc.Id, profilePath);
                        }
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "KillChromeForProfile failed");
        }
    }

    /// <summary>
    /// Removes Chrome's profile lock files so a new instance can start cleanly.
    /// Chrome creates these when it launches; leftover files from a crashed session
    /// cause new Chrome to attach to the old (already-dead) process instead of starting fresh.
    /// </summary>
    private static void ReleaseProfileLock(string profilePath)
    {
        var lockFiles = new[] { "lockfile", "SingletonLock", "SingletonSocket", "SingletonCookie" };
        foreach (var name in lockFiles)
        {
            var path = Path.Combine(profilePath, name);
            if (File.Exists(path))
                try { File.Delete(path); }
                catch { }
        }
    }

    private IWebDriver CreateDriver(string profilePath, string? proxyString)
    {
        Directory.CreateDirectory(profilePath);
        ReleaseProfileLock(profilePath);

        var options = new ChromeOptions();

        options.AddArgument("--no-first-run");
        options.AddArgument("--no-default-browser-check");
        options.AddArgument("--no-sandbox");                   // prevents DevToolsActivePort crash on Windows
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);

        options.AddArgument($"--user-data-dir={Path.GetFullPath(profilePath)}");

        // Apply proxy if set
        if (!string.IsNullOrWhiteSpace(proxyString))
        {
            var parts = proxyString.Trim().Split(':');
            if (parts.Length >= 2)
            {
                var host = parts[0];
                var port = parts[1];
                var user = parts.Length >= 3 ? parts[2] : null;
                var pass = parts.Length >= 4 ? parts[3] : null;

                if (user is not null)
                {
                    var relay = new HttpConnectRelay(host, int.Parse(port), user, pass);
                    options.AddArgument($"--proxy-server=http://127.0.0.1:{relay.LocalPort}");
                }
                else
                {
                    options.AddArgument($"--proxy-server=http://{host}:{port}");
                }
            }
        }

        if (!string.IsNullOrEmpty(_config.ChromiumPath))
            options.BinaryLocation = _config.ChromiumPath;

        var driverDir = string.IsNullOrEmpty(_config.ChromiumPath)
            ? null
            : Path.GetDirectoryName(_config.ChromiumPath);

        return driverDir is not null
            ? new ChromeDriver(driverDir, options)
            : new ChromeDriver(options);
    }

    private void RemoveDriver(int accountId)
    {
        if (_browsers.TryRemove(accountId, out var driver))
        {
            try { driver.Quit(); }   catch { }
            try { driver.Dispose(); } catch { }
        }
    }

    private async Task LogResultAsync(int accountId, string platform, string postText, PublishResult result)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
            db.PublishLogs.Add(new PublishLog
            {
                AccountId    = accountId,
                Platform     = platform,
                Status       = result.Success ? "ok" : "fail",
                PostText     = postText.Length > 500 ? postText[..500] : postText,
                ErrorMessage = result.Error
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write publish log");
        }
    }

    /// <summary>
    /// Closes the Chrome instance for a specific account.
    /// Call this after all platforms for one Telegram message are published
    /// so the browser doesn't stay open between requests.
    /// </summary>
    public void CloseDriver(int accountId)
    {
        RemoveDriver(accountId);
        Log.Information("Browser closed [account #{AccountId}]", accountId);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _browsers.Keys.ToList())
            RemoveDriver(id);
        await Task.CompletedTask;
    }
}
