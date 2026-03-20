using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WpfBrowserWorker.Data;
using WpfBrowserWorker.Data.Entities;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.Api;

public static class WorkerApi
{
    public static void MapWorkerEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ApiKeyFilter>();

        // ── Status ──────────────────────────────────────────────
        api.MapGet("/status", (WorkerStateService state, WorkerConfig config, TaskQueue queue) =>
            Results.Ok(new
            {
                worker_id = config.WorkerId,
                is_running = state.IsRunning,
                active_browsers = state.ActiveTasks.Count,
                max_browsers = config.MaxBrowsers,
                tasks_queued = queue.Count,
                tasks_completed = state.TasksCompleted,
                tasks_failed = state.TasksFailed,
                uptime_seconds = state.UptimeSeconds,
                api_version = "1.0"
            }));

        // ── Tasks ────────────────────────────────────────────────

        // POST /api/tasks  — принять задание от веб-панели
        api.MapPost("/tasks", async (
            [FromBody] CreateTaskRequest req,
            WorkerDbContext db,
            TaskQueue queue,
            WorkerStateService state) =>
        {
            if (!state.IsRunning)
                return Results.Problem("Worker is not running", statusCode: 503);

            var entity = new StoredTask
            {
                TaskType = req.TaskType,
                AccountId = req.AccountId,
                ParamsJson = req.Params is not null ? JsonSerializer.Serialize(req.Params) : null,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };
            db.Tasks.Add(entity);
            await db.SaveChangesAsync();

            // Конвертируем в BrowserTask и кладём в очередь
            var task = entity.ToBrowserTask();
            await queue.EnqueueAsync(task);

            return Results.Ok(new { id = entity.Id, status = entity.Status });
        });

        // GET /api/tasks  — список задач
        api.MapGet("/tasks", async (
            WorkerDbContext db,
            [FromQuery] string? status,
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0) =>
        {
            var q = db.Tasks.AsNoTracking().OrderByDescending(t => t.CreatedAt);
            var filtered = status is not null ? q.Where(t => t.Status == status) : q;
            var items = await filtered.Skip(offset).Take(limit).ToListAsync();
            return Results.Ok(items.Select(t => t.ToResponse()));
        });

        // GET /api/tasks/{id}  — статус конкретной задачи
        api.MapGet("/tasks/{id:int}", async (int id, WorkerDbContext db) =>
        {
            var task = await db.Tasks.FindAsync(id);
            return task is null ? Results.NotFound() : Results.Ok(task.ToResponse());
        });

        // DELETE /api/tasks/{id}  — отменить ожидающую задачу
        api.MapDelete("/tasks/{id:int}", async (int id, WorkerDbContext db) =>
        {
            var task = await db.Tasks.FindAsync(id);
            if (task is null) return Results.NotFound();
            if (task.Status != "pending") return Results.BadRequest(new { error = "Only pending tasks can be cancelled" });
            task.Status = "cancelled";
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        // ── Accounts ─────────────────────────────────────────────

        api.MapGet("/accounts", async (WorkerDbContext db, [FromQuery] string? platform) =>
        {
            var q = db.Accounts.AsNoTracking().OrderByDescending(a => a.CreatedAt);
            var items = platform is not null ? await q.Where(a => a.Platform == platform).ToListAsync()
                                              : await q.ToListAsync();
            return Results.Ok(items.Select(a => a.ToResponse()));
        });

        api.MapGet("/accounts/{id:int}", async (int id, WorkerDbContext db) =>
        {
            var a = await db.Accounts.FindAsync(id);
            return a is null ? Results.NotFound() : Results.Ok(a.ToDetailResponse());
        });

        api.MapPost("/accounts", async ([FromBody] UpsertAccountRequest req, WorkerDbContext db) =>
        {
            var entity = new StoredAccount
            {
                Platform = req.Platform,
                Username = req.Username,
                Proxy = req.Proxy,
                CookiesJson = req.CookiesJson,
                FingerprintJson = req.FingerprintJson,
                Notes = req.Notes
            };
            db.Accounts.Add(entity);
            await db.SaveChangesAsync();
            return Results.Ok(entity.ToResponse());
        });

        api.MapPut("/accounts/{id:int}", async (int id, [FromBody] UpsertAccountRequest req, WorkerDbContext db) =>
        {
            var entity = await db.Accounts.FindAsync(id);
            if (entity is null) return Results.NotFound();
            entity.Platform = req.Platform ?? entity.Platform;
            entity.Username = req.Username ?? entity.Username;
            entity.Status = req.Status ?? entity.Status;
            entity.Proxy = req.Proxy ?? entity.Proxy;
            if (req.CookiesJson is not null) entity.CookiesJson = req.CookiesJson;
            if (req.FingerprintJson is not null) entity.FingerprintJson = req.FingerprintJson;
            if (req.Notes is not null) entity.Notes = req.Notes;
            await db.SaveChangesAsync();
            return Results.Ok(entity.ToResponse());
        });

        api.MapDelete("/accounts/{id:int}", async (int id, WorkerDbContext db) =>
        {
            var entity = await db.Accounts.FindAsync(id);
            if (entity is null) return Results.NotFound();
            db.Accounts.Remove(entity);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }
}

// ── DTO records ──────────────────────────────────────────────────────────────

public record CreateTaskRequest(
    string TaskType,
    int? AccountId,
    Dictionary<string, object>? Params);

public record UpsertAccountRequest(
    string? Platform,
    string? Username,
    string? Status,
    string? Proxy,
    string? CookiesJson,
    string? FingerprintJson,
    string? Notes);

// ── Mapping helpers ───────────────────────────────────────────────────────────

file static class Mappings
{
    public static BrowserTask ToBrowserTask(this StoredTask t)
    {
        TaskTarget target = new();
        TaskConfig config = new();

        if (t.ParamsJson is not null)
        {
            try
            {
                var doc = JsonDocument.Parse(t.ParamsJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("url", out var url)) target.Url = url.GetString();
                if (root.TryGetProperty("username", out var un)) target.Username = un.GetString();
                if (root.TryGetProperty("text", out var txt)) target.Text = txt.GetString();
                if (root.TryGetProperty("selector", out var sel)) target.Selector = sel.GetString();
                if (root.TryGetProperty("screenshot_on_complete", out var sc))
                    config.ScreenshotOnComplete = sc.GetBoolean();
            }
            catch { /* malformed params — ignore, task will fail gracefully */ }
        }

        return new BrowserTask
        {
            Id = t.Id,
            TaskType = t.TaskType,
            AccountId = t.AccountId,
            CreatedAt = t.CreatedAt,
            Target = target,
            Config = config
        };
    }

    public static object ToResponse(this StoredTask t) => new
    {
        t.Id,
        t.TaskType,
        t.AccountId,
        t.Status,
        t.ErrorMessage,
        t.DurationMs,
        t.CreatedAt,
        t.StartedAt,
        t.CompletedAt,
        result = t.ResultJson is not null
            ? JsonSerializer.Deserialize<object>(t.ResultJson)
            : null
    };

    public static object ToResponse(this StoredAccount a) => new
    {
        a.Id,
        a.Platform,
        a.Username,
        a.Status,
        a.Proxy,
        a.Notes,
        a.LastUsedAt,
        a.CreatedAt
    };

    public static object ToDetailResponse(this StoredAccount a) => new
    {
        a.Id,
        a.Platform,
        a.Username,
        a.Status,
        a.Proxy,
        a.CookiesJson,
        a.FingerprintJson,
        a.Notes,
        a.LastUsedAt,
        a.CreatedAt
    };
}
