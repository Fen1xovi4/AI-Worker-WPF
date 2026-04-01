using System.ComponentModel.DataAnnotations;

namespace WpfBrowserWorker.Data.Entities;

public class StoredAccount
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string Platform { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Username { get; set; } = string.Empty;

    // active, banned, cooldown
    [MaxLength(32)]
    public string Status { get; set; } = "active";

    // Which Chrome profile to use for this account
    public int? LinkedProfileId { get; set; }

    // format: host:port:user:pass  or  host:port
    [MaxLength(512)]
    public string? Proxy { get; set; }

    // JSON array of cookie objects
    public string? CookiesJson { get; set; }

    // JSON browser fingerprint
    public string? FingerprintJson { get; set; }

    public string? Notes { get; set; }

    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
