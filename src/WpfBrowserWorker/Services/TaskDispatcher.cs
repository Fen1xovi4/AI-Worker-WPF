using Serilog;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

/// <summary>
/// Читает задания из TaskQueue и передаёт в TaskExecutor.
/// Работает как фоновый цикл пока воркер запущен.
/// </summary>
public class TaskDispatcher
{
    private readonly TaskQueue _queue;
    private readonly TaskExecutor _executor;
    private readonly WorkerConfig _config;
    private readonly SemaphoreSlim _semaphore;
    private CancellationTokenSource? _cts;

    public TaskDispatcher(TaskQueue queue, TaskExecutor executor, WorkerConfig config)
    {
        _queue = queue;
        _executor = executor;
        _config = config;
        _semaphore = new SemaphoreSlim(config.MaxBrowsers, config.MaxBrowsers);
    }

    public void Start(CancellationToken externalToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        Task.Run(() => DispatchLoopAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        Log.Information("TaskDispatcher started (maxConcurrent={Max})", _config.MaxBrowsers);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var task = await _queue.DequeueAsync(ct);

                await _semaphore.WaitAsync(ct);

                _ = Task.Run(async () =>
                {
                    try { await _executor.ExecuteTaskAsync(task, ct); }
                    catch (Exception ex) { Log.Error(ex, "Unhandled error in task {Id}", task.Id); }
                    finally { _semaphore.Release(); }
                }, ct);
            }
        }
        catch (OperationCanceledException) { }

        Log.Information("TaskDispatcher stopped");
    }
}
