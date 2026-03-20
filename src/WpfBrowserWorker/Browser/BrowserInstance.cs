using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Serilog;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser;

public class BrowserInstance : IAsyncDisposable
{
    private IWebDriver? _driver;
    private readonly WorkerConfig _config;
    private readonly FingerprintGenerator _fingerprint;
    private bool _isIdle = true;
    private DateTime _lastUsedAt = DateTime.UtcNow;

    public string Username { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public bool IsIdle => _isIdle;
    public TimeSpan IdleFor => _isIdle ? DateTime.UtcNow - _lastUsedAt : TimeSpan.Zero;

    public IWebDriver Driver => _driver ?? throw new InvalidOperationException("Browser not initialized");

    public BrowserInstance(WorkerConfig config, FingerprintGenerator fingerprint)
    {
        _config = config;
        _fingerprint = fingerprint;
    }

    public async Task InitializeAsync(WorkerAccount account)
    {
        Username = account.Username;
        Platform = account.Platform;

        var options = BuildChromeOptions(account);
        var profileDir = Path.Combine("profiles", account.Id);
        Directory.CreateDirectory(profileDir);
        options.AddArgument($"--user-data-dir={Path.GetFullPath(profileDir)}");

        // Auto-download matching chromedriver
        var driverPath = string.IsNullOrEmpty(_config.ChromiumPath)
            ? null
            : Path.GetDirectoryName(_config.ChromiumPath);

        _driver = driverPath != null
            ? new ChromeDriver(driverPath, options)
            : new ChromeDriver(options);

        // Inject stealth script
        var stealthJs = _fingerprint.GetStealthScript(account.Fingerprint);
        ((IJavaScriptExecutor)_driver).ExecuteScript(stealthJs);

        // Load cookies
        if (account.Cookies.Any())
            await LoadCookiesAsync(account.Cookies, account.Platform);

        Log.Debug("Browser initialized for {Username} on {Platform}", account.Username, account.Platform);
    }

    public async Task NavigateAsync(string url, int timeoutMs = 30_000)
    {
        _driver!.Manage().Timeouts().PageLoad = TimeSpan.FromMilliseconds(timeoutMs);
        _driver.Navigate().GoToUrl(url);
        await Task.Delay(500);  // brief settle
    }

    public async Task LoadCookiesAsync(IEnumerable<BrowserCookie> cookies, string platform)
    {
        // Must navigate to domain before setting cookies
        var domain = platform switch
        {
            "instagram" => "https://www.instagram.com",
            "twitter" => "https://x.com",
            "tiktok" => "https://www.tiktok.com",
            "linkedin" => "https://www.linkedin.com",
            "threads" => "https://www.threads.net",
            _ => throw new ArgumentException($"Unknown platform: {platform}")
        };

        _driver!.Navigate().GoToUrl(domain);
        await Task.Delay(1000);

        _driver.Manage().Cookies.DeleteAllCookies();
        foreach (var cookie in cookies)
        {
            try
            {
                _driver.Manage().Cookies.AddCookie(new Cookie(
                    cookie.Name, cookie.Value, cookie.Domain,
                    cookie.Path, cookie.Expiry));
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to add cookie {Name}: {Error}", cookie.Name, ex.Message);
            }
        }
    }

    public List<BrowserCookie> GetCurrentCookies()
    {
        return _driver?.Manage().Cookies.AllCookies
            .Select(c => new BrowserCookie
            {
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Path = c.Path,
                Secure = c.Secure,
                HttpOnly = c.IsHttpOnly,
                Expiry = c.Expiry
            }).ToList() ?? new List<BrowserCookie>();
    }

    public async Task<string?> TakeScreenshotBase64Async()
    {
        try
        {
            var ss = ((ITakesScreenshot)_driver!).GetScreenshot();
            return ss.AsBase64EncodedString;
        }
        catch (Exception ex)
        {
            Log.Warning("Screenshot failed: {Error}", ex.Message);
            return null;
        }
    }

    public void MarkBusy()
    {
        _isIdle = false;
        _lastUsedAt = DateTime.UtcNow;
    }

    public void MarkIdle()
    {
        _isIdle = true;
        _lastUsedAt = DateTime.UtcNow;
    }

    private ChromeOptions BuildChromeOptions(WorkerAccount account)
    {
        var options = new ChromeOptions();

        // Anti-detection
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);

        // Fingerprint
        _fingerprint.ApplyFingerprint(options, account.Fingerprint, account.UserAgent);

        // Proxy
        if (account.Proxy != null)
        {
            var proxy = new Proxy { Kind = ProxyKind.Manual };
            if (account.Proxy.Type == "socks5")
                proxy.SocksProxy = $"{account.Proxy.Host}:{account.Proxy.Port}";
            else
                proxy.HttpProxy = $"{account.Proxy.Host}:{account.Proxy.Port}";
            options.Proxy = proxy;
        }

        // Stability
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-gpu");

        if (!string.IsNullOrEmpty(_config.ChromiumPath))
            options.BinaryLocation = _config.ChromiumPath;

        return options;
    }

    public async ValueTask DisposeAsync()
    {
        try { _driver?.Quit(); } catch { }
        try { _driver?.Dispose(); } catch { }
        _driver = null;
        await Task.CompletedTask;
    }
}
