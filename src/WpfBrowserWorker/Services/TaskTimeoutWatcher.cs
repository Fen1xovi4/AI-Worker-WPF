using Serilog;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public class TaskTimeoutWatcher
{
    private readonly WorkerStateService _state;
    private readonly IApiClient _apiClient;
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);
    private Timer? _timer;

    public TaskTimeoutWatcher(WorkerStateService state, IApiClient apiClient)
    {
        _state = state;
        _apiClient = apiClient;
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

            Log.Warning("Task {TaskId} has been running for {Age} — marking as timed out", taskId, task.Age);
            _state.UntrackTask(taskId);

            await _apiClient.UpdateTaskStatusAsync(taskId, new TaskStatusUpdate
            {
                Status = "failed",
                Error = $"Local timeout after {_timeout.TotalMinutes} minutes"
            });
        }
    }
}
