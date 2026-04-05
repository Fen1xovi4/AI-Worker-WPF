using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WpfBrowserWorker.Data;
using WpfBrowserWorker.Data.Entities;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

public class LocalTaskService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TaskQueue _queue;

    public LocalTaskService(IServiceScopeFactory scopeFactory, TaskQueue queue)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    public async Task<List<LocalScheduledTask>> GetAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        return await db.LocalScheduledTasks.OrderByDescending(t => t.CreatedAt).ToListAsync();
    }

    public async Task<LocalScheduledTask> CreateAsync(LocalScheduledTask task)
    {
        task.CreatedAt = DateTime.UtcNow;
        task.NextRunAt = task.RepeatMode == "once" ? null : ComputeNextRunAt(task, DateTime.Now);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        db.LocalScheduledTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task UpdateAsync(LocalScheduledTask task)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        db.LocalScheduledTasks.Update(task);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var entity = await db.LocalScheduledTasks.FindAsync(id);
        if (entity is null) return;
        db.LocalScheduledTasks.Remove(entity);
        await db.SaveChangesAsync();
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    public async Task RunNowAsync(LocalScheduledTask task, CancellationToken ct = default)
    {
        Log.Information("LocalTaskService: RunNow [{TaskType}] '{Name}' on {Platform}",
            task.TaskType, task.Name, task.Platform);

        // 1. Create StoredTask record so TaskExecutor can update its status
        int storedTaskId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
            var stored = new StoredTask
            {
                TaskType  = task.TaskType,
                AccountId = task.AccountId,
                Status    = "pending",
                CreatedAt = DateTime.UtcNow,
                ParamsJson = JsonSerializer.Serialize(new { task.Name, task.Platform, task.TargetUrl, task.Count })
            };
            db.Tasks.Add(stored);
            await db.SaveChangesAsync(ct);
            storedTaskId = stored.Id;
        }

        // 2. Build BrowserTask
        var browserTask = new BrowserTask
        {
            Id        = storedTaskId,
            TaskType  = task.TaskType,
            Platform  = task.Platform,
            AccountId = task.AccountId,
            CreatedAt = DateTime.UtcNow,
            Target    = BuildTarget(task)
        };

        // 3. Enqueue
        await _queue.EnqueueAsync(browserTask);
    }

    // ── Schedule ──────────────────────────────────────────────────────────────

    /// <summary>Calculates the next DateTime when this task should run, based on RepeatMode + DaysJson + TimeOfDay.</summary>
    public static DateTime? ComputeNextRunAt(LocalScheduledTask task, DateTime from)
    {
        if (task.RepeatMode == "once")
            return null;

        if (task.RepeatMode == "hourly")
            return from.AddHours(1);

        // daily or weekly — find next matching day at TimeOfDay
        var days = ParseDays(task.DaysJson);
        var tod  = ParseTimeOfDay(task.TimeOfDay);

        if (task.RepeatMode == "daily")
            return NextOccurrence(from, days, tod, weeklyStep: false);

        if (task.RepeatMode == "weekly")
            return NextOccurrence(from, days, tod, weeklyStep: true);

        return from.AddDays(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TaskTarget BuildTarget(LocalScheduledTask task)
    {
        return task.TaskType switch
        {
            "like" => new TaskTarget
            {
                Url          = string.IsNullOrWhiteSpace(task.TargetUrl) ? FeedUrl(task.Platform) : task.TargetUrl,
                CollectPosts = true
            },
            "follow" or "unfollow" => new TaskTarget
            {
                Username = task.TargetUrl?.TrimStart('@')
            },
            "scroll_feed" => new TaskTarget
            {
                Url             = FeedUrl(task.Platform),
                DurationSeconds = task.Count * 3
            },
            "view_story" => new TaskTarget
            {
                Url = string.IsNullOrWhiteSpace(task.TargetUrl) ? StoriesUrl(task.Platform) : task.TargetUrl
            },
            _ => new TaskTarget { Url = FeedUrl(task.Platform) }
        };
    }

    private static string FeedUrl(string platform) => platform == "threads"
        ? "https://www.threads.net/"
        : "https://www.instagram.com/";

    private static string StoriesUrl(string platform) => platform == "threads"
        ? "https://www.threads.net/"
        : "https://www.instagram.com/stories/";

    private static List<int> ParseDays(string json)
    {
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? new(); }
        catch { return new(); }
    }

    private static TimeSpan ParseTimeOfDay(string hhmm)
    {
        if (TimeSpan.TryParseExact(hhmm, @"hh\:mm", null, out var ts))
            return ts;
        return TimeSpan.FromHours(9);
    }

    /// <summary>
    /// Finds the next DateTime >= (from + epsilon) that:
    ///  - falls on a day in <paramref name="days"/> (or any day if list empty)
    ///  - has the time component == <paramref name="tod"/>
    ///  - if weeklyStep: minimum gap of 7 days from <paramref name="from"/>
    /// Days are stored as 1=Mon..7=Sun.
    /// </summary>
    private static DateTime NextOccurrence(DateTime from, List<int> days, TimeSpan tod, bool weeklyStep)
    {
        var candidate = from.Date.Add(tod);
        if (candidate <= from) candidate = candidate.AddDays(1);
        if (weeklyStep) candidate = from.Date.AddDays(7).Add(tod);

        for (int i = 0; i < 14; i++)
        {
            if (days.Count == 0 || days.Contains(ToDayNumber(candidate.DayOfWeek)))
                return candidate;
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    // Maps .NET DayOfWeek → 1=Mon..7=Sun
    private static int ToDayNumber(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday    => 1,
        DayOfWeek.Tuesday   => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Thursday  => 4,
        DayOfWeek.Friday    => 5,
        DayOfWeek.Saturday  => 6,
        DayOfWeek.Sunday    => 7,
        _                   => 1
    };
}
