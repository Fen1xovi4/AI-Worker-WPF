using System.Text.Json.Serialization;

namespace WpfBrowserWorker.Models;

public class BrowserTask
{
    public string Id { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public int Priority { get; set; }
    public TaskTarget Target { get; set; } = new();
    public TaskConfig Config { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int RetryCount { get; set; }
}

public class TaskTarget
{
    public string? Url { get; set; }
    public string? Selector { get; set; }
    public string? Text { get; set; }
    public string? Username { get; set; }
    public string? Action { get; set; }
    public int? DurationSeconds { get; set; }
    public bool? CollectPosts { get; set; }
}

public class TaskConfig
{
    public int HumanDelayMinMs { get; set; } = 500;
    public int HumanDelayMaxMs { get; set; } = 2000;
    public bool ScreenshotOnComplete { get; set; }
}
