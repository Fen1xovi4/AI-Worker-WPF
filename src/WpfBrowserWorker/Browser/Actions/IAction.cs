using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public interface IAction
{
    string TaskType { get; }
    Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct);
}
