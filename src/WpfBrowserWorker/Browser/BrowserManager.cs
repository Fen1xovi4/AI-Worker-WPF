using System.Collections.Concurrent;
using Serilog;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser;

public class BrowserManager : IBrowserManager, IAsyncDisposable
{
    private readonly WorkerConfig _config;
    private readonly FingerprintGenerator _fingerprint;
    private readonly ConcurrentDictionary<string, BrowserInstance> _browsers = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly Timer _idleTimer;

    public int ActiveCount => _browsers.Count(b => !b.Value.IsIdle);

    public BrowserManager(WorkerConfig config, FingerprintGenerator fingerprint)
    {
        _config = config;
        _fingerprint = fingerprint;
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

            var instance = new BrowserInstance(_config, _fingerprint);
            await instance.InitializeAsync(account);
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
}
