using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Tests;

public static class Fixtures
{
    public static BrowserTask FakeTask(string type = "like") => new()
    {
        Id = Guid.NewGuid().ToString(),
        TaskType = type,
        Platform = "instagram",
        AccountId = "acc-test-1",
        Priority = 5,
        Target = new TaskTarget { Url = "https://www.instagram.com/p/test123/" },
        Config = new TaskConfig { HumanDelayMinMs = 0, HumanDelayMaxMs = 0 }
    };

    public static WorkerAccount FakeAccount() => new()
    {
        Id = "acc-test-1",
        Platform = "instagram",
        Username = "test_bot",
        Cookies = new List<BrowserCookie>(),
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
        Fingerprint = new BrowserFingerprint { ScreenWidth = 1920, ScreenHeight = 1080 },
        Config = new AccountLimits()
    };

    public static WorkerConfig FakeConfig() => new()
    {
        BackendUrl = "http://localhost:9090",
        ApiKey = "wk_testkey123456789012345678901234567890123456",
        WorkerId = "worker-test-1",
        PollIntervalMs = 100,
        MaxBrowsers = 2,
        HumanMode = false  // disable delays in tests
    };
}
