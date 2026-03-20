namespace WpfBrowserWorker.Models;

public class HeartbeatRequest
{
    public string WorkerId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public int ActiveBrowsers { get; set; }
    public int MaxBrowsers { get; set; }
    public List<string> ActiveAccounts { get; set; } = new();
    public double CpuPercent { get; set; }
    public long MemoryMb { get; set; }
    public int TasksCompleted { get; set; }
    public int TasksFailed { get; set; }
    public long UptimeSeconds { get; set; }
}

public class HeartbeatResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime ServerTime { get; set; }
    public List<string> Commands { get; set; } = new();
}

public class CookieUpdateRequest
{
    public List<BrowserCookie> Cookies { get; set; } = new();
    public DateTime LastActionAt { get; set; } = DateTime.UtcNow;
}

public class UploadRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string Type { get; set; } = "screenshot";  // screenshot | har_log | page_source
    public byte[] FileBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
}

public class UploadResponse
{
    public string Url { get; set; } = string.Empty;
}
