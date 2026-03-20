namespace WpfBrowserWorker.Models;

public class TaskResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ScreenshotBase64 { get; set; }
    public Dictionary<string, object>? ExtractedData { get; set; }
    public List<ActionLogStep> ActionLog { get; set; } = new();
    public int DurationMs { get; set; }
    public string? SpecialStatus { get; set; }  // "captcha", "account_locked", "rate_limited"

    public static TaskResult Succeed(List<ActionLogStep>? log = null) => new()
    {
        Success = true,
        ActionLog = log ?? new()
    };

    public static TaskResult Fail(string error, string? screenshotBase64 = null) => new()
    {
        Success = false,
        Error = error,
        ScreenshotBase64 = screenshotBase64
    };

    public static TaskResult Special(string status, string? screenshotBase64 = null) => new()
    {
        Success = false,
        SpecialStatus = status,
        ScreenshotBase64 = screenshotBase64
    };

    public TaskStatusUpdate ToStatusUpdate() => new()
    {
        Status = SpecialStatus ?? (Success ? "done" : "failed"),
        Error = Error,
        ScreenshotBase64 = ScreenshotBase64,
        DurationMs = DurationMs,
        Result = Success ? new TaskResultData
        {
            Success = true,
            ExtractedData = ExtractedData,
            ActionLog = ActionLog
        } : null
    };
}

public class ActionLogStep
{
    public string Step { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Selector { get; set; }
    public bool? Found { get; set; }
    public bool? ActionSuccess { get; set; }
    public int DurationMs { get; set; }
}

public class TaskStatusUpdate
{
    public string Status { get; set; } = string.Empty;
    public TaskResultData? Result { get; set; }
    public string? Error { get; set; }
    public string? ScreenshotBase64 { get; set; }
    public int? DurationMs { get; set; }
}

public class TaskResultData
{
    public bool Success { get; set; }
    public string? ScreenshotBase64 { get; set; }
    public Dictionary<string, object>? ExtractedData { get; set; }
    public List<ActionLogStep> ActionLog { get; set; } = new();
}
