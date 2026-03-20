namespace WpfBrowserWorker.Models;

public class WorkerAccount
{
    public string Id { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public List<BrowserCookie> Cookies { get; set; } = new();
    public ProxyConfig? Proxy { get; set; }
    public string UserAgent { get; set; } = string.Empty;
    public BrowserFingerprint Fingerprint { get; set; } = new();
    public AccountLimits Config { get; set; } = new();
}

public class BrowserCookie
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public DateTime? Expiry { get; set; }
}

public class ProxyConfig
{
    public string Type { get; set; } = "http";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }  // never log

    public string ToProxyUrl() => string.IsNullOrEmpty(Username)
        ? $"{Type}://{Host}:{Port}"
        : $"{Type}://{Username}:{Password}@{Host}:{Port}";
}

public class BrowserFingerprint
{
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public string Timezone { get; set; } = "Europe/Moscow";
    public string Language { get; set; } = "ru-RU";
    public string? WebglVendor { get; set; }
    public string Platform { get; set; } = "Win32";
}

public class AccountLimits
{
    public int MaxActionsPerHour { get; set; } = 30;
    public int MaxActionsPerDay { get; set; } = 200;
    public int[] CooldownAfterActionMs { get; set; } = { 2000, 5000 };
}
