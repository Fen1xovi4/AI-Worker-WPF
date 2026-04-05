using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfBrowserWorker.Data.Entities;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.ViewModels;

public partial class TaskListViewModel : ObservableObject
{
    private readonly WorkerStateService _state;
    private readonly LocalTaskService   _localTaskService;
    private readonly ProfileService     _profileService;

    // ── External tasks (from API) ─────────────────────────────────────────
    public ObservableCollection<TaskDisplayItem> ActiveTasks { get; } = new();

    // ── Local tasks (from DB) ─────────────────────────────────────────────
    public ObservableCollection<LocalTaskItem> LocalTasks { get; } = new();

    // ── Accounts for dropdown ─────────────────────────────────────────────
    public ObservableCollection<AccountOption> Accounts { get; } = new();

    // ── Static lists ──────────────────────────────────────────────────────
    public static IReadOnlyList<string> TaskTypes   { get; } = ["Like posts", "Follow", "Unfollow", "Scroll feed", "View stories"];
    public static IReadOnlyList<string> RepeatModes { get; } = ["Один раз", "Каждый час", "Ежедневно", "По дням"];

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

    public bool IsLocalTab    { get => SelectedTab == 0; set { if (value) SelectedTab = 0; } }
    public bool IsExternalTab { get => SelectedTab == 1; set { if (value) SelectedTab = 1; } }

    public Visibility LocalTabVisibility      => SelectedTab == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExternalTabVisibility   => SelectedTab == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NewTaskButtonVisibility => SelectedTab == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── Add Task form ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddFormVisibility), nameof(EmptyLocalVisibility))]
    private bool _isAddingTask;

    public Visibility AddFormVisibility    => IsAddingTask                          ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyLocalVisibility => LocalTasks.Count == 0 && !IsAddingTask ? Visibility.Visible : Visibility.Collapsed;

    // ── Form fields ───────────────────────────────────────────────────────
    [ObservableProperty] private string _newTaskName    = string.Empty;
    [ObservableProperty] private string _newTaskType    = "Like posts";
    [ObservableProperty] private string _newTaskTarget  = string.Empty;
    [ObservableProperty] private string _newTaskCount   = "50";
    [ObservableProperty] private string _newTaskRepeat  = "Один раз";
    [ObservableProperty] private string _newTaskTime    = "09:00";
    [ObservableProperty] private bool   _newTaskPlatformInstagram = true;
    [ObservableProperty] private bool   _newTaskPlatformThreads;
    [ObservableProperty] private int?   _newTaskAccountId;

    // Days of week (1=Mon..7=Sun)
    [ObservableProperty] private bool _dayMon;
    [ObservableProperty] private bool _dayTue;
    [ObservableProperty] private bool _dayWed;
    [ObservableProperty] private bool _dayThu;
    [ObservableProperty] private bool _dayFri;
    [ObservableProperty] private bool _daySat;
    [ObservableProperty] private bool _daySun;

    // Visibility helpers for schedule fields
    partial void OnNewTaskRepeatChanged(string value)   => OnPropertyChanged(nameof(ShowDaysRow));
    partial void OnNewTaskTypeChanged(string value)     => OnPropertyChanged(nameof(ShowTargetField));

    public Visibility ShowDaysRow    => (NewTaskRepeat == "Ежедневно" || NewTaskRepeat == "По дням" || NewTaskRepeat == "Каждый час")
                                            ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowTargetField => NewTaskType != "Scroll feed"
                                            ? Visibility.Visible : Visibility.Collapsed;

    // ── Constructor ───────────────────────────────────────────────────────
    public TaskListViewModel(WorkerStateService state, LocalTaskService localTaskService, ProfileService profileService)
    {
        _state            = state;
        _localTaskService = localTaskService;
        _profileService   = profileService;

        _state.StateChanged += (_, _) => RefreshExternal();
        SelectedTab = 0;
        RefreshExternal();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var tasks    = await _localTaskService.GetAllAsync();
        var accounts = await _profileService.GetAllAccountsAsync();

        var acctMap = accounts.ToDictionary(a => a.Id, a => $"{a.Username} ({a.Platform})");

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Accounts.Clear();
            Accounts.Add(new AccountOption { Id = null, Label = "— любой —" });
            foreach (var a in accounts)
                Accounts.Add(new AccountOption { Id = a.Id, Label = $"{a.Username} ({a.Platform})" });

            LocalTasks.Clear();
            foreach (var t in tasks)
            {
                var label = t.AccountId.HasValue && acctMap.TryGetValue(t.AccountId.Value, out var lbl) ? lbl : "—";
                LocalTasks.Add(new LocalTaskItem(t) { AccountDisplayLabel = label });
            }

            OnPropertyChanged(nameof(EmptyLocalVisibility));
        });
    }

    // ── Local task commands ───────────────────────────────────────────────
    [RelayCommand]
    private void AddTask()
    {
        IsAddingTask = true;
        OnPropertyChanged(nameof(EmptyLocalVisibility));
    }

    [RelayCommand]
    private async Task SaveTask()
    {
        if (string.IsNullOrWhiteSpace(NewTaskName)) return;

        var days = new List<int>();
        if (DayMon) days.Add(1);
        if (DayTue) days.Add(2);
        if (DayWed) days.Add(3);
        if (DayThu) days.Add(4);
        if (DayFri) days.Add(5);
        if (DaySat) days.Add(6);
        if (DaySun) days.Add(7);

        var task = new LocalScheduledTask
        {
            Name       = NewTaskName.Trim(),
            TaskType   = MapTaskType(NewTaskType),
            Platform   = NewTaskPlatformThreads ? "threads" : "instagram",
            AccountId  = NewTaskAccountId,
            TargetUrl  = string.IsNullOrWhiteSpace(NewTaskTarget) ? null : NewTaskTarget.Trim(),
            Count      = int.TryParse(NewTaskCount, out var c) && c > 0 ? c : 50,
            RepeatMode = MapRepeatMode(NewTaskRepeat),
            DaysJson   = JsonSerializer.Serialize(days),
            TimeOfDay  = NewTaskTime,
            IsActive   = true
        };

        var saved = await _localTaskService.CreateAsync(task);
        var acctLabel = Accounts.FirstOrDefault(a => a.Id == saved.AccountId)?.Label ?? "—";
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            LocalTasks.Add(new LocalTaskItem(saved) { AccountDisplayLabel = acctLabel });
            ResetForm();
            OnPropertyChanged(nameof(EmptyLocalVisibility));
        });
    }

    [RelayCommand]
    private void CancelTask()
    {
        ResetForm();
        IsAddingTask = false;
        OnPropertyChanged(nameof(EmptyLocalVisibility));
    }

    [RelayCommand]
    private async Task DeleteLocalTask(LocalTaskItem? item)
    {
        if (item is null) return;
        await _localTaskService.DeleteAsync(item.Entity.Id);
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            LocalTasks.Remove(item);
            OnPropertyChanged(nameof(EmptyLocalVisibility));
        });
    }

    [RelayCommand]
    private async Task RunTask(LocalTaskItem? item)
    {
        if (item is null) return;
        await _localTaskService.RunNowAsync(item.Entity);
    }

    [RelayCommand]
    private async Task TogglePause(LocalTaskItem? item)
    {
        if (item is null) return;
        item.Entity.IsActive = !item.Entity.IsActive;
        if (item.Entity.IsActive && item.Entity.RepeatMode != "once")
            item.Entity.NextRunAt = LocalTaskService.ComputeNextRunAt(item.Entity, DateTime.Now);
        await _localTaskService.UpdateAsync(item.Entity);
        item.RefreshStatus();
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

    // ── Helpers ───────────────────────────────────────────────────────────
    private void ResetForm()
    {
        NewTaskName    = string.Empty;
        NewTaskType    = "Like posts";
        NewTaskTarget  = string.Empty;
        NewTaskCount   = "50";
        NewTaskRepeat  = "Один раз";
        NewTaskTime    = "09:00";
        NewTaskPlatformInstagram = true;
        NewTaskPlatformThreads   = false;
        NewTaskAccountId = null;
        DayMon = DayTue = DayWed = DayThu = DayFri = DaySat = DaySun = false;
        IsAddingTask = false;
    }

    private static string MapTaskType(string ui) => ui switch
    {
        "Like posts"   => "like",
        "Follow"       => "follow",
        "Unfollow"     => "unfollow",
        "Scroll feed"  => "scroll_feed",
        "View stories" => "view_story",
        _              => "like"
    };

    private static string MapRepeatMode(string ui) => ui switch
    {
        "Каждый час"  => "hourly",
        "Ежедневно"   => "daily",
        "По дням"     => "daily",
        _             => "once"
    };
}

// ── Account dropdown option ───────────────────────────────────────────────────
public class AccountOption
{
    public int?   Id    { get; set; }
    public string Label { get; set; } = string.Empty;
}

// ── Local task display wrapper ────────────────────────────────────────────────
public partial class LocalTaskItem : ObservableObject
{
    public LocalScheduledTask Entity { get; }
    public string AccountDisplayLabel { get; init; } = "—";

    public LocalTaskItem(LocalScheduledTask entity) => Entity = entity;

    public string Name         => Entity.Name;
    public string TaskTypeLabel => Entity.TaskType switch
    {
        "like"        => "Like posts",
        "follow"      => "Follow",
        "unfollow"    => "Unfollow",
        "scroll_feed" => "Scroll feed",
        "view_story"  => "View stories",
        _             => Entity.TaskType
    };
    public string PlatformLabel => Entity.Platform == "threads" ? "Threads" : "Instagram";
    public string AccountLabel  => Entity.AccountId.HasValue ? $"#{Entity.AccountId}" : "—";
    public string CountLabel    => $"{Entity.Count}×";
    public string ScheduleLabel
    {
        get
        {
            var mode = Entity.RepeatMode switch
            {
                "once"    => "Один раз",
                "hourly"  => "Кажд. час",
                "daily"   => string.IsNullOrEmpty(Entity.DaysJson) || Entity.DaysJson == "[]"
                                 ? "Ежедневно"
                                 : $"По дням {Entity.TimeOfDay}",
                "weekly"  => $"Еженед. {Entity.TimeOfDay}",
                _         => Entity.RepeatMode
            };
            return mode;
        }
    }
    public string NextRunLabel => Entity.NextRunAt.HasValue
        ? Entity.NextRunAt.Value.ToLocalTime().ToString("dd.MM HH:mm")
        : "—";

    public string StatusLabel => Entity.IsActive ? "Активно" : "Пауза";
    public string PauseIcon   => Entity.IsActive ? "⏸" : "▶";

    public SolidColorBrush TypeBadgeBrush => Entity.TaskType switch
    {
        "like"        => Brush("#E5C07B"),
        "follow"      => Brush("#98C379"),
        "unfollow"    => Brush("#E06C75"),
        "scroll_feed" => Brush("#61AFEF"),
        "view_story"  => Brush("#C678DD"),
        _             => Brush("#636D83")
    };

    public SolidColorBrush PlatformBrush => Entity.Platform == "threads"
        ? Brush("#ABB2BF") : Brush("#E5C07B");

    public SolidColorBrush StatusBrush => Entity.IsActive ? Brush("#98C379") : Brush("#636D83");

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(PauseIcon));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(NextRunLabel));
    }

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
