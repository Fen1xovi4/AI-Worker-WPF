using System.Collections.Concurrent;
using System.IO;
using Serilog;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.Browser;

public class BrowserManager : IBrowserManager, IAsyncDisposable
{
    private readonly WorkerConfig _config;
    private readonly FingerprintGenerator _fingerprint;
    private readonly ProfileService _profileService;
    private readonly ConcurrentDictionary<string, BrowserInstance> _browsers = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly Timer _idleTimer;

    public int ActiveCount => _browsers.Count(b => !b.Value.IsIdle);

    public BrowserManager(WorkerConfig config, FingerprintGenerator fingerprint, ProfileService profileService)
    {
        _config = config;
        _fingerprint = fingerprint;
        _profileService = profileService;
        _semaphore = new SemaphoreSlim(config.MaxBrowsers, config.MaxBrowsers);
        _idleTimer = new Timer(async _ => await CloseIdleBrowsersAsync(),
            null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task<BrowserInstance> GetOrCreateBrowserAsync(string accountId, WorkerAccount account)
    {
        if (_browsers.TryGetValue(accountId, out var existing))
        {
            existing.MarkBusy();
            return existing;
        }

        await _semaphore.WaitAsync();
        try
        {
            // Double-check after acquiring semaphore
            if (_browsers.TryGetValue(accountId, out existing))
            {
                existing.MarkBusy();
                return existing;
            }

            string profilePath;
            if (int.TryParse(accountId, out var numericId))
            {
                var resolution = await _profileService.ResolveProfileAsync(numericId);
                profilePath = resolution.ProfilePath;

                // Apply profile proxy if account has no proxy of its own
                if (account.Proxy is null && resolution.Proxy is not null)
                    account.Proxy = ParseProxy(resolution.Proxy);
            }
            else
            {
                profilePath = Path.GetFullPath(Path.Combine("profiles", accountId));
            }

            var instance = new BrowserInstance(_config, _fingerprint);
            await instance.InitializeAsync(account, profilePath);
            _browsers[accountId] = instance;

            Log.Information("Opened browser for account {AccountId} ({Username})",
                accountId, account.Username);

            return instance;
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    public Task ReleaseBrowserAsync(string accountId)
    {
        if (_browsers.TryGetValue(accountId, out var browser))
            browser.MarkIdle();
        return Task.CompletedTask;
    }

    public async Task CloseIdleBrowsersAsync()
    {
        var idleTimeout = TimeSpan.FromMinutes(10);
        var toClose = _browsers
            .Where(b => b.Value.IsIdle && b.Value.IdleFor > idleTimeout)
            .Select(b => b.Key)
            .ToList();

        foreach (var accountId in toClose)
        {
            if (_browsers.TryRemove(accountId, out var browser))
            {
                await browser.DisposeAsync();
                _semaphore.Release();
                Log.Information("Closed idle browser for account {AccountId}", accountId);
            }
        }
    }

    public IEnumerable<BrowserStatusItem> GetBrowserStatuses() =>
        _browsers.Select(b => new BrowserStatusItem
        {
            AccountId = b.Key,
            Username = b.Value.Username,
            Platform = b.Value.Platform,
            Status = b.Value.IsIdle ? "idle" : "running"
        });

    public async ValueTask DisposeAsync()
    {
        _idleTimer.Dispose();
        foreach (var (_, browser) in _browsers)
            await browser.DisposeAsync();
        _browsers.Clear();
    }

    // Parses proxy string: host:port  or  host:port:user:pass
    // Prefix socks5:// or http:// is also accepted.
    private static ProxyConfig? ParseProxy(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var type = "http";
        var s = raw.Trim();

        if (s.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
        {
            type = "socks5";
            s = s["socks5://".Length..];
        }
        else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            s = s["http://".Length..];
        }

        var parts = s.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var port))
        {
            Log.Warning("Invalid proxy format '{Proxy}', skipping", raw);
            return null;
        }

        return new ProxyConfig
        {
            Type = type,
            Host = parts[0],
            Port = port,
            Username = parts.Length >= 3 ? parts[2] : null,
            Password = parts.Length >= 4 ? parts[3] : null
        };
    }
}
