namespace WpfBrowserWorker.Models;

public class WorkerConfig
{
    public string WorkerId { get; set; } = string.Empty;
    public int ApiPort { get; set; } = 5000;
    public string ApiKey { get; set; } = string.Empty;
    public int MaxBrowsers { get; set; } = 3;
    public string? ChromiumPath { get; set; }
    public string ScreenshotsPath { get; set; } = "./screenshots/";
    public bool HumanMode { get; set; } = true;
    public bool AutoStart { get; set; } = false;
    public string LogLevel { get; set; } = "Information";
    public string DatabasePath { get; set; } = "./worker.db";
}
