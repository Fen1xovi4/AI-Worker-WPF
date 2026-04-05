using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WpfBrowserWorker.Data;

namespace WpfBrowserWorker.Services;

/// <summary>
/// Checks every 60 seconds for local scheduled tasks that are due and enqueues them.
/// </summary>
public class SchedulerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalTaskService _localTaskService;
    private Timer? _timer;

    public SchedulerService(IServiceScopeFactory scopeFactory, LocalTaskService localTaskService)
    {
        _scopeFactory      = scopeFactory;
        _localTaskService  = localTaskService;
    }

    public void Start()
    {
        _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
        Log.Information("SchedulerService started (interval 60s)");
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? _)
    {
        _ = RunDueTasksAsync();
    }

    private async Task RunDueTasksAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

            var now = DateTime.UtcNow;
            var due = await db.LocalScheduledTasks
                .Where(t => t.IsActive && t.NextRunAt != null && t.NextRunAt <= now)
                .ToListAsync();

            foreach (var task in due)
            {
                try
                {
                    Log.Information("Scheduler: running '{Name}' ({TaskType})", task.Name, task.TaskType);
                    await _localTaskService.RunNowAsync(task);

                    task.LastRunAt = now;

                    if (task.RepeatMode == "once")
                    {
                        task.IsActive  = false;
                        task.NextRunAt = null;
                    }
                    else
                    {
                        task.NextRunAt = LocalTaskService.ComputeNextRunAt(task, DateTime.Now);
                    }

                    db.LocalScheduledTasks.Update(task);
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Scheduler: error running task '{Name}'", task.Name);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Scheduler: unexpected error in OnTick");
        }
    }
}
