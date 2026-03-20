using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WpfBrowserWorker.Data;

namespace WpfBrowserWorker.Services;

public class TaskTimeoutWatcher
{
    private readonly WorkerStateService _state;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);
    private Timer? _timer;

    public TaskTimeoutWatcher(WorkerStateService state, IServiceScopeFactory scopeFactory)
    {
        _state = state;
        _scopeFactory = scopeFactory;
    }

    public void Start() =>
        _timer = new Timer(async _ => await CheckAsync(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    public void Stop() => _timer?.Dispose();

    private async Task CheckAsync()
    {
        foreach (var (taskId, task) in _state.ActiveTasks)
        {
            if (task.Age <= _timeout) continue;

            Log.Warning("Task {TaskId} timed out after {Age}", taskId, task.Age);
            _state.UntrackTask(taskId);

            if (!int.TryParse(taskId, out var id)) continue;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

            var entity = await db.Tasks.FindAsync(id);
            if (entity is null) continue;

            entity.Status = "failed";
            entity.ErrorMessage = $"Timeout after {_timeout.TotalMinutes} minutes";
            entity.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
