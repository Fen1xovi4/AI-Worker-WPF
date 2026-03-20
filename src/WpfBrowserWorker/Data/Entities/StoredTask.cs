using System.ComponentModel.DataAnnotations;

namespace WpfBrowserWorker.Data.Entities;

public class StoredTask
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TaskType { get; set; } = string.Empty;

    public int? AccountId { get; set; }

    // JSON payload from web panel
    public string? ParamsJson { get; set; }

    // pending → running → completed / failed / cancelled
    [MaxLength(32)]
    public string Status { get; set; } = "pending";

    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ScreenshotBase64 { get; set; }

    public int DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
