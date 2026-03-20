using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WpfBrowserWorker.Browser;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly WorkerStateService _state;
    private readonly IBrowserManager _browserManager;

    [ObservableProperty] private int _tasksDone;
    [ObservableProperty] private int _tasksFailed;
    [ObservableProperty] private int _tasksActive;

    public ObservableCollection<BrowserStatusItem> ActiveBrowsers { get; } = new();

    public DashboardViewModel(WorkerStateService state, IBrowserManager browserManager)
    {
        _state = state;
        _browserManager = browserManager;
        _state.StateChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            TasksDone = _state.TasksCompleted;
            TasksFailed = _state.TasksFailed;
            TasksActive = _state.ActiveTasks.Count;

            ActiveBrowsers.Clear();
            foreach (var b in _browserManager.GetBrowserStatuses())
                ActiveBrowsers.Add(b);
        });
    }
}
