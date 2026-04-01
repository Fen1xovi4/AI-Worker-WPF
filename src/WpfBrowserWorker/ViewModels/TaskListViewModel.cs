using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.ViewModels;

public partial class TaskListViewModel : ObservableObject
{
    private readonly WorkerStateService _state;

    // ── External tasks (received from API) ────────────────────────────────
    public ObservableCollection<TaskDisplayItem> ActiveTasks { get; } = new();

    // ── Local tasks (created in-app) ──────────────────────────────────────
    public ObservableCollection<LocalTaskItem> LocalTasks { get; } = new();

    public static IReadOnlyList<string> TaskTypes   { get; } = ["Like posts", "Follow", "Unfollow", "Scroll feed", "View stories"];
    public static IReadOnlyList<string> RepeatModes { get; } = ["Один раз", "Каждый час", "Ежедневно", "Еженедельно"];

    // ── Tab selection ─────────────────────────────────────────────────────
    [ObservableProperty]
    private int _selectedTab; // 0 = Local, 1 = External

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsLocalTab));
        OnPropertyChanged(nameof(IsExternalTab));
        OnPropertyChanged(nameof(LocalTabVisibility));
        OnPropertyChanged(nameof(ExternalTabVisibility));
        OnPropertyChanged(nameof(NewTaskButtonVisibility));
    }

    public bool IsLocalTab
    {
        get => SelectedTab == 0;
        set { if (value) SelectedTab = 0; }
    }
    public bool IsExternalTab
    {
        get => SelectedTab == 1;
        set { if (value) SelectedTab = 1; }
    }

    public Visibility LocalTabVisibility      => SelectedTab == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExternalTabVisibility   => SelectedTab == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NewTaskButtonVisibility => SelectedTab == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── Add Task form ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddFormVisibility), nameof(EmptyLocalVisibility))]
    private bool _isAddingTask;

    public Visibility AddFormVisibility   => IsAddingTask                ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyLocalVisibility => LocalTasks.Count == 0 && !IsAddingTask ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] private string _newTaskName    = string.Empty;
    [ObservableProperty] private string _newTaskType    = "Like posts";
    [ObservableProperty] private string _newTaskAccount = string.Empty;
    [ObservableProperty] private string _newTaskCount   = "50";
    [ObservableProperty] private string _newTaskRepeat  = "Один раз";

    // ── Constructor ───────────────────────────────────────────────────────
    public TaskListViewModel(WorkerStateService state)
    {
        _state = state;
        _state.StateChanged += (_, _) => RefreshExternal();
        SelectedTab = 0;
        RefreshExternal();
    }

    // ── Local task commands ───────────────────────────────────────────────
    [RelayCommand]
    private void AddTask()
    {
        IsAddingTask = true;
        OnPropertyChanged(nameof(EmptyLocalVisibility));
    }

    [RelayCommand]
    private void SaveTask()
    {
        if (string.IsNullOrWhiteSpace(NewTaskName)) return;

        LocalTasks.Add(new LocalTaskItem
        {
            Name         = NewTaskName.Trim(),
            TaskType     = NewTaskType,
            AccountLabel = string.IsNullOrWhiteSpace(NewTaskAccount) ? "—" : NewTaskAccount.Trim(),
            Count        = int.TryParse(NewTaskCount, out var c) && c > 0 ? c : 50,
            RepeatMode   = NewTaskRepeat,
            IsActive     = true
        });

        NewTaskName    = string.Empty;
        NewTaskAccount = string.Empty;
        NewTaskCount   = "50";
        IsAddingTask   = false;
        OnPropertyChanged(nameof(EmptyLocalVisibility));
    }

    [RelayCommand]
    private void CancelTask()
    {
        IsAddingTask = false;
        OnPropertyChanged(nameof(EmptyLocalVisibility));
    }

    [RelayCommand]
    private void DeleteLocalTask(LocalTaskItem? item)
    {
        if (item is null) return;
        LocalTasks.Remove(item);
        OnPropertyChanged(nameof(EmptyLocalVisibility));
    }

    [RelayCommand]
    private static void RunTask(LocalTaskItem? item)
    {
        // TODO: wire up browser action execution
        if (item is not null) item.IsActive = true;
    }

    [RelayCommand]
    private static void TogglePause(LocalTaskItem? item)
    {
        if (item is not null) item.IsActive = !item.IsActive;
    }

    // ── External refresh ──────────────────────────────────────────────────
    private void RefreshExternal()
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ActiveTasks.Clear();
            foreach (var t in _state.ActiveTasks.Values.OrderByDescending(x => x.StartedAt))
                ActiveTasks.Add(new TaskDisplayItem
                {
                    TaskId    = t.TaskId,
                    AccountId = t.AccountId,
                    TaskType  = t.TaskType,
                    Status    = "running",
                    Duration  = $"{(int)t.Age.TotalSeconds}s"
                });
        });
    }
}

// ── Local task model ──────────────────────────────────────────────────────────
public partial class LocalTaskItem : ObservableObject
{
    public Guid   Id           { get; } = Guid.NewGuid();
    public string Name         { get; set; } = string.Empty;
    public string TaskType     { get; set; } = "Like posts";
    public string AccountLabel { get; set; } = "—";
    public int    Count        { get; set; } = 50;
    public string RepeatMode   { get; set; } = "Один раз";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush), nameof(StatusLabel), nameof(PauseIcon))]
    private bool _isActive = true;

    public SolidColorBrush TypeBadgeBrush => TaskType switch
    {
        "Like posts"   => Brush("#E5C07B"),
        "Follow"       => Brush("#98C379"),
        "Unfollow"     => Brush("#E06C75"),
        "Scroll feed"  => Brush("#61AFEF"),
        "View stories" => Brush("#C678DD"),
        _              => Brush("#636D83")
    };

    public string         CountLabel  => $"{Count}×";
    public string         RepeatLabel => RepeatMode;
    public string         StatusLabel => IsActive ? "Активно" : "Пауза";
    public string         PauseIcon   => IsActive ? "⏸" : "▶";
    public SolidColorBrush StatusBrush => IsActive ? Brush("#98C379") : Brush("#636D83");

    private static readonly ColorConverter _cc = new();
    private static SolidColorBrush Brush(string hex) =>
        new((Color)_cc.ConvertFrom(hex)!);
}

// ── External task display model ───────────────────────────────────────────────
public class TaskDisplayItem
{
    public string TaskId    { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string TaskType  { get; set; } = string.Empty;
    public string Status    { get; set; } = string.Empty;
    public string Duration  { get; set; } = string.Empty;
    public string StatusIcon => Status switch
    {
        "done"    => "✅",
        "failed"  => "❌",
        "running" => "🔄",
        _         => "⏳"
    };
}
