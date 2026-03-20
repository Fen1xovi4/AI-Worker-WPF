using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public class ApiClient : IApiClient
{
    private readonly HttpClient _http;
    private readonly WorkerConfig _config;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ApiClient(HttpClient http, WorkerConfig config)
    {
        _http = http;
        _config = config;
    }

    public async Task<HeartbeatResponse> SendHeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/workers/heartbeat", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HeartbeatResponse>(JsonOptions, ct)
               ?? new HeartbeatResponse { Status = "ok" };
    }

    public async Task<List<BrowserTask>> PollTasksAsync(int limit = 5, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/workers/tasks?limit={limit}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<BrowserTask>>(JsonOptions, ct)
               ?? new List<BrowserTask>();
    }

    public async Task UpdateTaskStatusAsync(string taskId, TaskStatusUpdate update, CancellationToken ct = default)
    {
        var content = JsonContent.Create(update, options: JsonOptions);
        var response = await _http.PatchAsync($"/api/v1/workers/tasks/{taskId}", content, ct);
        if (!response.IsSuccessStatusCode)
            Log.Warning("Failed to update task {TaskId} status: {StatusCode}", taskId, response.StatusCode);
    }

    public async Task<WorkerAccount> GetAccountAsync(string accountId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/workers/accounts/{accountId}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkerAccount>(JsonOptions, ct)
               ?? throw new InvalidOperationException($"Account {accountId} returned null");
    }

    public async Task SaveCookiesAsync(string accountId, CookieUpdateRequest request, CancellationToken ct = default)
    {
        var content = JsonContent.Create(request, options: JsonOptions);
        var response = await _http.PutAsync($"/api/v1/workers/accounts/{accountId}/cookies", content, ct);
        if (!response.IsSuccessStatusCode)
            Log.Warning("Failed to save cookies for account {AccountId}", accountId);
    }

    public async Task<UploadResponse> UploadArtifactAsync(UploadRequest request, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(request.TaskId), "task_id");
        form.Add(new StringContent(request.Type), "type");
        form.Add(new ByteArrayContent(request.FileBytes), "file", request.FileName);

        var response = await _http.PostAsync("/api/v1/workers/upload", form, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UploadResponse>(JsonOptions, ct)
               ?? new UploadResponse();
    }
}
