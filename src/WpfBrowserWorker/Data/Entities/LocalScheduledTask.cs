using System.ComponentModel.DataAnnotations;

namespace WpfBrowserWorker.Data.Entities;

public class LocalScheduledTask
{
    public int Id { get; set; }

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    // like / follow / unfollow / scroll_feed / view_story
    [MaxLength(64)]
    public string TaskType { get; set; } = "like";

    // instagram / threads
    [MaxLength(64)]
    public string Platform { get; set; } = "instagram";

    public int? AccountId { get; set; }

    // URL or @username target (null = use feed)
    [MaxLength(512)]
    public string? TargetUrl { get; set; }

    // number of actions or scroll duration multiplier
    public int Count { get; set; } = 50;

    // once / hourly / daily / weekly
    [MaxLength(32)]
    public string RepeatMode { get; set; } = "once";

    // JSON int array, 1=Mon..7=Sun, e.g. "[1,2,3,4,5]"
    public string DaysJson { get; set; } = "[]";

    // "HH:mm"
    [MaxLength(8)]
    public string TimeOfDay { get; set; } = "09:00";

    public bool IsActive { get; set; } = true;

    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
