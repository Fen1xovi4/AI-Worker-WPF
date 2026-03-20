using System.Diagnostics;
using Serilog;
using WpfBrowserWorker.Browser;
using WpfBrowserWorker.Browser.Actions;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public class TaskExecutor
{
    private readonly IApiClient _apiClient;
    private readonly IBrowserManager _browserManager;
    private readonly WorkerStateService _state;
    private readonly Dictionary<string, IAction> _actions;

    public TaskExecutor(IApiClient apiClient, IBrowserManager browserManager,
        WorkerStateService state, IEnumerable<IAction> actions)
    {
        _apiClient = apiClient;
        _browserManager = browserManager;
        _state = state;
        _actions = actions.ToDictionary(a => a.TaskType, a => a);
    }

    public async Task ExecuteTaskAsync(Models.BrowserTask task, CancellationToken ct)
    {
        Log.Information("Starting task {TaskType} [{TaskId}] for account {AccountId}",
            task.TaskType, task.Id, task.AccountId);

        _state.TrackTask(task.Id, task.AccountId, task.TaskType);

        try
        {
            await _apiClient.UpdateTaskStatusAsync(task.Id,
                new TaskStatusUpdate { Status = "running" }, ct);

            if (!_actions.TryGetValue(task.TaskType, out var action))
            {
                await FailTask(task, $"Unknown task type: {task.TaskType}", null, ct);
                return;
            }

            var account = await _apiClient.GetAccountAsync(task.AccountId, ct);
            var browser = await _browserManager.GetOrCreateBrowserAsync(task.AccountId, account);

            var sw = Stopwatch.StartNew();
            TaskResult result;

            try
            {
                result = await action.ExecuteAsync(task, browser, ct);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in action {TaskType}", task.TaskType);
                var screenshot = await browser.TakeScreenshotBase64Async();
                result = TaskResult.Fail(ex.Message, screenshot);
            }

            sw.Stop();
            result.DurationMs = (int)sw.ElapsedMilliseconds;

            await _apiClient.UpdateTaskStatusAsync(task.Id, result.ToStatusUpdate(), ct);
            await _apiClient.SaveCookiesAsync(task.AccountId, new CookieUpdateRequest
            {
                Cookies = browser.GetCurrentCookies(),
                LastActionAt = DateTime.UtcNow
            }, ct);

            _state.RecordTaskResult(result);

            Log.Information("Task {TaskId} {Status} in {DurationMs}ms",
                task.Id, result.Success ? "done" : "failed", result.DurationMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fatal error executing task {TaskId}", task.Id);
            await FailTask(task, ex.Message, null, ct);
        }
        finally
        {
            _state.UntrackTask(task.Id);
        }
    }

    private async Task FailTask(Models.BrowserTask task, string error,
        string? screenshot, CancellationToken ct)
    {
        _state.RecordTaskResult(TaskResult.Fail(error, screenshot));
        await _apiClient.UpdateTaskStatusAsync(task.Id, new TaskStatusUpdate
        {
            Status = "failed",
            Error = error,
            ScreenshotBase64 = screenshot
        }, ct);
    }
}
