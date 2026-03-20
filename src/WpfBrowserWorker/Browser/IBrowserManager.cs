using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser;

public interface IBrowserManager
{
    int ActiveCount { get; }
    Task<BrowserInstance> GetOrCreateBrowserAsync(string accountId, WorkerAccount account);
    Task ReleaseBrowserAsync(string accountId);
    Task CloseIdleBrowsersAsync();
    IEnumerable<BrowserStatusItem> GetBrowserStatuses();
}

public class BrowserStatusItem
{
    public string AccountId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Status { get; set; } = "idle";  // idle | running | warming_up
    public string? CurrentTask { get; set; }
}
