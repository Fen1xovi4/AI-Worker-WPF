using System.Collections.Concurrent;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public class WorkerStateService
{
    private int _tasksCompleted;
    private int _tasksFailed;
    private DateTime _startedAt;
    private bool _isRunning;
    private bool _isConnected;

    public bool IsRunning => _isRunning;
    public bool IsConnected => _isConnected;
    public int TasksCompleted => _tasksCompleted;
    public int TasksFailed => _tasksFailed;
    public long UptimeSeconds => _isRunning ? (long)(DateTime.UtcNow - _startedAt).TotalSeconds : 0;

    public ConcurrentDictionary<string, RunningTask> ActiveTasks { get; } = new();

    public event EventHandler<WorkerStateChangedEventArgs>? StateChanged;

    public void StartWorker()
    {
        _isRunning = true;
        _startedAt = DateTime.UtcNow;
        RaiseStateChanged();
    }

    public void StopWorker()
    {
        _isRunning = false;
        _isConnected = false;
        RaiseStateChanged();
    }

    public void SetConnected(bool connected)
    {
        _isConnected = connected;
        RaiseStateChanged();
    }

    public void RecordTaskResult(TaskResult result)
    {
        if (result.Success)
            Interlocked.Increment(ref _tasksCompleted);
        else
            Interlocked.Increment(ref _tasksFailed);
        RaiseStateChanged();
    }

    public void TrackTask(string taskId, string accountId, string taskType)
    {
        ActiveTasks[taskId] = new RunningTask
        {
            TaskId = taskId,
            AccountId = accountId,
            TaskType = taskType,
            StartedAt = DateTime.UtcNow
        };
    }

    public void UntrackTask(string taskId) => ActiveTasks.TryRemove(taskId, out _);

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(this, new WorkerStateChangedEventArgs());
}

public class RunningTask
{
    public string TaskId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public TimeSpan Age => DateTime.UtcNow - StartedAt;
}

public class WorkerStateChangedEventArgs : EventArgs { }
