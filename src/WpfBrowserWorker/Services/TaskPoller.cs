using Serilog;
using WpfBrowserWorker.Browser;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public class TaskPoller
{
    private readonly IApiClient _apiClient;
    private readonly TaskExecutor _executor;
    private readonly WorkerConfig _config;
    private readonly IBrowserManager _browserManager;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    public TaskPoller(IApiClient apiClient, TaskExecutor executor,
        WorkerConfig config, IBrowserManager browserManager)
    {
        _apiClient = apiClient;
        _executor = executor;
        _config = config;
        _browserManager = browserManager;
    }

    public void Start(CancellationToken externalCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_pollTask != null)
            await Task.WhenAny(_pollTask, Task.Delay(5000));
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var backoffFactor = 1.0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_browserManager.ActiveCount >= _config.MaxBrowsers)
                {
                    await Task.Delay(2000, ct);
                    continue;
                }

                var tasks = await _apiClient.PollTasksAsync(
                    limit: _config.MaxBrowsers - _browserManager.ActiveCount, ct);

                if (tasks.Count > 0)
                {
                    backoffFactor = 1.0;
                    foreach (var task in tasks)
                        _ = _executor.ExecuteTaskAsync(task, ct);
                }
                else
                {
                    backoffFactor = Math.Min(backoffFactor * 1.5, 4.0);
                }

                var jitter = Random.Shared.Next(-(_config.PollIntervalMs / 5), _config.PollIntervalMs / 5);
                var delay = (int)(_config.PollIntervalMs * backoffFactor) + jitter;
                await Task.Delay(Math.Min(delay, 30_000), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Task poll error");
                await Task.Delay(10_000, ct);
            }
        }
    }
}
