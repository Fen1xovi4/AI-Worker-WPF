namespace WpfBrowserWorker.Models;

public class WorkerConfig
{
    public string BackendUrl { get; set; } = "https://your-backend.com";
    public string ApiKey { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    public int PollIntervalMs { get; set; } = 7000;
    public int MaxBrowsers { get; set; } = 3;
    public string? ChromiumPath { get; set; }
    public string ScreenshotsPath { get; set; } = "./screenshots/";
    public bool HumanMode { get; set; } = true;
    public bool AutoStart { get; set; } = false;
    public string LogLevel { get; set; } = "Information";
}
