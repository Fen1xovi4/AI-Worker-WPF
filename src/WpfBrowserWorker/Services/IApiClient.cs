using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public interface IApiClient
{
    Task<HeartbeatResponse> SendHeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default);
    Task<List<BrowserTask>> PollTasksAsync(int limit = 5, CancellationToken ct = default);
    Task UpdateTaskStatusAsync(string taskId, TaskStatusUpdate update, CancellationToken ct = default);
    Task<WorkerAccount> GetAccountAsync(string accountId, CancellationToken ct = default);
    Task SaveCookiesAsync(string accountId, CookieUpdateRequest request, CancellationToken ct = default);
    Task<UploadResponse> UploadArtifactAsync(UploadRequest request, CancellationToken ct = default);
}
