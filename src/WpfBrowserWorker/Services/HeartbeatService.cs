using System.Net.Http;
using Serilog;
using WpfBrowserWorker.Browser;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public class HeartbeatService
{
    private readonly IApiClient _apiClient;
    private readonly WorkerConfig _config;
    private readonly WorkerStateService _state;
    private readonly IBrowserManager _browserManager;
    private Timer? _timer;

    public HeartbeatService(IApiClient apiClient, WorkerConfig config,
        WorkerStateService state, IBrowserManager browserManager)
    {
        _apiClient = apiClient;
        _config = config;
        _state = state;
        _browserManager = browserManager;
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _apiClient.SendHeartbeatAsync(BuildRequest(), ct);
            _state.SetConnected(response.Status == "ok");
            return _state.IsConnected;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Log.Error("Invalid API key — worker cannot connect");
            _state.SetConnected(false);
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Heartbeat failed");
            _state.SetConnected(false);
            return false;
        }
    }

    public void Start()
    {
        _timer = new Timer(async _ => await BeatAsync(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task BeatAsync()
    {
        try
        {
            var response = await _apiClient.SendHeartbeatAsync(BuildRequest());
            _state.SetConnected(response.Status == "ok");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Heartbeat error");
            _state.SetConnected(false);
        }
    }

    private HeartbeatRequest BuildRequest()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        return new HeartbeatRequest
        {
            WorkerId = _config.WorkerId,
            Version = "1.0.0",
            Hostname = Environment.MachineName,
            ActiveBrowsers = _browserManager.ActiveCount,
            MaxBrowsers = _config.MaxBrowsers,
            ActiveAccounts = _state.ActiveTasks.Values.Select(t => t.AccountId).Distinct().ToList(),
            CpuPercent = 0,  // TODO: System.Diagnostics.PerformanceCounter
            MemoryMb = process.WorkingSet64 / 1024 / 1024,
            TasksCompleted = _state.TasksCompleted,
            TasksFailed = _state.TasksFailed,
            UptimeSeconds = _state.UptimeSeconds
        };
    }
}
