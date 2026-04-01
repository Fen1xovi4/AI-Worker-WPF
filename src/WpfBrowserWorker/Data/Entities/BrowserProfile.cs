using System.ComponentModel.DataAnnotations;

namespace WpfBrowserWorker.Data.Entities;

public class BrowserProfile
{
    public int Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string ProfilePath { get; set; } = string.Empty;

    // format: host:port  or  host:port:user:pass  (same convention as StoredAccount.Proxy)
    [MaxLength(512)]
    public string? Proxy { get; set; }

    public bool ProxyEnabled { get; set; } = true;

    // active | archived | error
    [MaxLength(32)]
    public string Status { get; set; } = "active";

    // nullable — set when this profile is assigned to an account
    public int? LinkedAccountId { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
