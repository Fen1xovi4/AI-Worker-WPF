using System.ComponentModel.DataAnnotations;

namespace WpfBrowserWorker.Data.Entities;

public class ProfilePage
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    // "instagram" | "threads" | "facebook" | "tiktok" | "twitter" | "x" | "other"
    [MaxLength(64)]
    public string Platform { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Label { get; set; }

    // "ru" | "en" | "de" | "fr" | "es" — language for AI content generation
    [MaxLength(8)]
    public string Language { get; set; } = "ru";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public StoredAccount Account { get; set; } = null!;
}
