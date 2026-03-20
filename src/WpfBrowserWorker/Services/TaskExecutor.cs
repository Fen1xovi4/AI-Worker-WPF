using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WpfBrowserWorker.Browser;
using WpfBrowserWorker.Browser.Actions;
using WpfBrowserWorker.Data;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public class TaskExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBrowserManager _browserManager;
    private readonly WorkerStateService _state;
    private readonly Dictionary<string, IAction> _actions;

    public TaskExecutor(
        IServiceScopeFactory scopeFactory,
        IBrowserManager browserManager,
        WorkerStateService state,
        IEnumerable<IAction> actions)
    {
        _scopeFactory = scopeFactory;
        _browserManager = browserManager;
        _state = state;
        _actions = actions.ToDictionary(a => a.TaskType, a => a);
    }

    public async Task ExecuteTaskAsync(BrowserTask task, CancellationToken ct)
    {
        Log.Information("Starting task {TaskType} [{TaskId}] for account {AccountId}",
            task.TaskType, task.Id, task.AccountId);

        _state.TrackTask(task.Id.ToString(), task.AccountId?.ToString() ?? "none", task.TaskType);

        try
        {
            await UpdateStatusAsync(task.Id, "running", null, null, null, ct);

            if (!_actions.TryGetValue(task.TaskType, out var action))
            {
                await FailTaskAsync(task.Id, $"Unknown task type: {task.TaskType}", null, ct);
                return;
            }

            // Get account from local SQLite
            var account = task.AccountId.HasValue
                ? await GetAccountAsync(task.AccountId.Value, ct)
                : null;

            var browser = await _browserManager.GetOrCreateBrowserAsync(
                task.AccountId?.ToString() ?? "anonymous",
                account);

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

            // Save cookies back to SQLite
            if (task.AccountId.HasValue)
            {
                var cookiesJson = JsonSerializer.Serialize(browser.GetCurrentCookies());
                await SaveCookiesAsync(task.AccountId.Value, cookiesJson, ct);
            }

            await UpdateStatusAsync(
                task.Id,
                result.Success ? "completed" : "failed",
                result.Success ? JsonSerializer.Serialize(new { result.ExtractedData, result.ActionLog }) : null,
                result.Error,
                result.ScreenshotBase64,
                ct,
                result.DurationMs);

            _state.RecordTaskResult(result);

            Log.Information("Task {TaskId} {Status} in {DurationMs}ms",
                task.Id, result.Success ? "completed" : "failed", result.DurationMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fatal error executing task {TaskId}", task.Id);
            await FailTaskAsync(task.Id, ex.Message, null, ct);
        }
        finally
        {
            _state.UntrackTask(task.Id.ToString());
        }
    }

    private async Task UpdateStatusAsync(
        int taskId, string status,
        string? resultJson, string? error, string? screenshot,
        CancellationToken ct, int durationMs = 0)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        var entity = await db.Tasks.FindAsync([taskId], ct);
        if (entity is null) return;

        entity.Status = status;
        entity.ErrorMessage = error;
        entity.ResultJson = resultJson;
        entity.ScreenshotBase64 = screenshot;
        if (durationMs > 0) entity.DurationMs = durationMs;

        if (status == "running") entity.StartedAt = DateTime.UtcNow;
        else if (status is "completed" or "failed") entity.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private async Task FailTaskAsync(int taskId, string error, string? screenshot, CancellationToken ct)
    {
        _state.RecordTaskResult(TaskResult.Fail(error, screenshot));
        await UpdateStatusAsync(taskId, "failed", null, error, screenshot, ct);
    }

    private async Task<WorkerAccount?> GetAccountAsync(int accountId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        var entity = await db.Accounts.FindAsync([accountId], ct);
        if (entity is null) return null;

        var account = new WorkerAccount
        {
            Id = entity.Id.ToString(),
            Platform = entity.Platform,
            Username = entity.Username,
        };

        if (entity.CookiesJson is not null)
        {
            try { account.Cookies = JsonSerializer.Deserialize<List<BrowserCookie>>(entity.CookiesJson) ?? new(); }
            catch { /* ignore malformed cookies */ }
        }

        if (entity.FingerprintJson is not null)
        {
            try { account.Fingerprint = JsonSerializer.Deserialize<BrowserFingerprint>(entity.FingerprintJson) ?? new(); }
            catch { }
        }

        if (entity.Proxy is not null)
        {
            // format: host:port  or  host:port:user:pass
            var parts = entity.Proxy.Split(':');
            account.Proxy = parts.Length >= 2 ? new ProxyConfig
            {
                Host = parts[0],
                Port = int.TryParse(parts[1], out var p) ? p : 8080,
                Username = parts.Length >= 3 ? parts[2] : null,
                Password = parts.Length >= 4 ? parts[3] : null
            } : null;
        }

        return account;
    }

    private async Task SaveCookiesAsync(int accountId, string? cookiesJson, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cookiesJson)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        var entity = await db.Accounts.FindAsync([accountId], ct);
        if (entity is null) return;

        entity.CookiesJson = cookiesJson;
        entity.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
