using System.ComponentModel.DataAnnotations;

namespace WpfBrowserWorker.Data.Entities;

public class PublishLog
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    [MaxLength(64)]
    public string Platform { get; set; } = string.Empty;

    // "ok" | "fail"
    [MaxLength(16)]
    public string Status { get; set; } = "ok";

    public string? PostText { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}
