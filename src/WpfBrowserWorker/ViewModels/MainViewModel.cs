using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Media;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WorkerStateService _state;
    private readonly TaskDispatcher _dispatcher;
    private readonly TaskTimeoutWatcher _timeoutWatcher;
    private readonly WorkerConfig _config;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _workerStatus = "Stopped";
    [ObservableProperty] private string _workerId = string.Empty;
    [ObservableProperty] private string _appVersion = "v1.0.0";
    [ObservableProperty] private Brush _statusBarColor = Brushes.DimGray;
    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private bool _isRunning;

    public MainViewModel(
        WorkerStateService state,
        TaskDispatcher dispatcher,
        TaskTimeoutWatcher timeoutWatcher,
        WorkerConfig config,
        IServiceProvider services)
    {
        _state = state;
        _dispatcher = dispatcher;
        _timeoutWatcher = timeoutWatcher;
        _config = config;
        _services = services;

        WorkerId = config.WorkerId;
        _state.StateChanged += (_, _) => UpdateFromState();

        CurrentView = services.GetRequiredService<DashboardViewModel>();
    }

    public void Initialize()
    {
        if (_config.AutoStart)
            _ = StartWorkerAsync();
    }

    [RelayCommand]
    private void Navigate(string destination)
    {
        CurrentView = destination switch
        {
            "Dashboard" => _services.GetRequiredService<DashboardViewModel>(),
            "Tasks"     => _services.GetRequiredService<TaskListViewModel>(),
            "Accounts"  => _services.GetRequiredService<AccountsViewModel>(),
            "Logs"      => _services.GetRequiredService<LogsViewModel>(),
            "Settings"  => _services.GetRequiredService<SettingsViewModel>(),
            _           => CurrentView
        };
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartWorkerAsync()
    {
        _cts = new CancellationTokenSource();
        _state.StartWorker();
        _dispatcher.Start(_cts.Token);
        _timeoutWatcher.Start();
        IsRunning = true;
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopWorkerAsync()
    {
        _cts?.Cancel();
        _dispatcher.Stop();
        _timeoutWatcher.Stop();
        _state.StopWorker();
        IsRunning = false;
        return Task.CompletedTask;
    }

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    private void UpdateFromState()
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_state.IsRunning)
            {
                WorkerStatus = $"Running — ✅ {_state.TasksCompleted}  ❌ {_state.TasksFailed}";
                StatusBarColor = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
            else
            {
                WorkerStatus = "Stopped";
                StatusBarColor = new SolidColorBrush(Color.FromRgb(75, 85, 99));
            }

            StartWorkerCommand.NotifyCanExecuteChanged();
            StopWorkerCommand.NotifyCanExecuteChanged();
        });
    }
}
