using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.ViewModels;

public partial class TaskListViewModel : ObservableObject
{
    private readonly WorkerStateService _state;

    public ObservableCollection<TaskDisplayItem> ActiveTasks { get; } = new();

    public TaskListViewModel(WorkerStateService state)
    {
        _state = state;
        _state.StateChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ActiveTasks.Clear();
            foreach (var t in _state.ActiveTasks.Values.OrderByDescending(x => x.StartedAt))
            {
                ActiveTasks.Add(new TaskDisplayItem
                {
                    TaskId = t.TaskId,
                    AccountId = t.AccountId,
                    TaskType = t.TaskType,
                    Status = "running",
                    Duration = $"{(int)t.Age.TotalSeconds}s"
                });
            }
        });
    }
}

public class TaskDisplayItem
{
    public string TaskId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string StatusIcon => Status switch
    {
        "done" => "✅",
        "failed" => "❌",
        "running" => "🔄",
        _ => "⏳"
    };
}
