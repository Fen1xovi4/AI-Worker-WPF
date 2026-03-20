using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Media;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Services;
using WpfBrowserWorker.Views;

namespace WpfBrowserWorker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WorkerStateService _state;
    private readonly HeartbeatService _heartbeat;
    private readonly TaskPoller _poller;
    private readonly TaskTimeoutWatcher _timeoutWatcher;
    private readonly WorkerConfig _config;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _workerStatus = "Disconnected";
    [ObservableProperty] private string _workerId = string.Empty;
    [ObservableProperty] private string _appVersion = "v1.0.0";
    [ObservableProperty] private Brush _statusBarColor = Brushes.DimGray;
    [ObservableProperty] private Brush _connectionDot = Brushes.Gray;
    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private bool _isRunning;

    public MainViewModel(WorkerStateService state, HeartbeatService heartbeat,
        TaskPoller poller, TaskTimeoutWatcher timeoutWatcher,
        WorkerConfig config, IServiceProvider services)
    {
        _state = state;
        _heartbeat = heartbeat;
        _poller = poller;
        _timeoutWatcher = timeoutWatcher;
        _config = config;

        WorkerId = config.WorkerId;

        _state.StateChanged += (_, _) => UpdateFromState();

        // Default to dashboard view
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
        // Views are created via DI
        CurrentView = destination switch
        {
            "Dashboard" => App.Services.GetRequiredService<DashboardViewModel>(),
            "Tasks" => App.Services.GetRequiredService<TaskListViewModel>(),
            "Accounts" => App.Services.GetRequiredService<AccountsViewModel>(),
            "Logs" => App.Services.GetRequiredService<LogsViewModel>(),
            "Settings" => App.Services.GetRequiredService<SettingsViewModel>(),
            _ => CurrentView
        };
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartWorkerAsync()
    {
        _cts = new CancellationTokenSource();
        _state.StartWorker();

        WorkerStatus = "Connecting...";
        StatusBarColor = new SolidColorBrush(Color.FromRgb(202, 138, 4));

        var connected = await _heartbeat.ValidateConnectionAsync(_cts.Token);
        if (!connected)
        {
            WorkerStatus = "Connection failed";
            StatusBarColor = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            _state.StopWorker();
            return;
        }

        _heartbeat.Start();
        _poller.Start(_cts.Token);
        _timeoutWatcher.Start();
        IsRunning = true;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopWorkerAsync()
    {
        _cts?.Cancel();
        await _poller.StopAsync();
        _heartbeat.Stop();
        _timeoutWatcher.Stop();
        _state.StopWorker();
        IsRunning = false;
    }

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    private void UpdateFromState()
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_state.IsConnected)
            {
                WorkerStatus = $"Connected — ✅ {_state.TasksCompleted} done  ❌ {_state.TasksFailed} failed";
                StatusBarColor = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                ConnectionDot = new SolidColorBrush(Color.FromRgb(74, 222, 128));
            }
            else if (_state.IsRunning)
            {
                WorkerStatus = "Reconnecting...";
                StatusBarColor = new SolidColorBrush(Color.FromRgb(202, 138, 4));
                ConnectionDot = new SolidColorBrush(Color.FromRgb(250, 204, 21));
            }
            else
            {
                WorkerStatus = "Stopped";
                StatusBarColor = new SolidColorBrush(Color.FromRgb(75, 85, 99));
                ConnectionDot = Brushes.Gray;
            }

            StartWorkerCommand.NotifyCanExecuteChanged();
            StopWorkerCommand.NotifyCanExecuteChanged();
        });
    }
}
