using OpenQA.Selenium;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Helpers;

public class HumanBehaviorSimulator
{
    private readonly WorkerConfig _config;

    public HumanBehaviorSimulator(WorkerConfig config) => _config = config;

    public async Task MicroDelayAsync(CancellationToken ct = default)
    {
        if (!_config.HumanMode) return;
        await Task.Delay(Random.Shared.Next(200, 800), ct);
    }

    public async Task ActionDelayAsync(int minMs, int maxMs, CancellationToken ct = default)
    {
        if (!_config.HumanMode) return;
        await Task.Delay(Random.Shared.Next(Math.Max(minMs, 100), Math.Max(maxMs, 200)), ct);
    }

    public async Task MoveAndClickAsync(IWebDriver driver, IWebElement element, CancellationToken ct = default)
    {
        if (_config.HumanMode)
        {
            var actions = new OpenQA.Selenium.Interactions.Actions(driver);
            var offsetX = Random.Shared.Next(-5, 5);
            var offsetY = Random.Shared.Next(-3, 3);
            actions.MoveToElement(element, offsetX, offsetY)
                   .Pause(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 400)))
                   .Click()
                   .Perform();
        }
        else
        {
            element.Click();
        }

        await Task.Delay(100, ct);
    }

    public async Task TypeAsync(IWebElement element, string text, CancellationToken ct = default)
    {
        if (!_config.HumanMode)
        {
            element.SendKeys(text);
            return;
        }

        foreach (var c in text)
        {
            ct.ThrowIfCancellationRequested();
            element.SendKeys(c.ToString());
            var delay = Random.Shared.Next(50, 150);
            if (Random.Shared.Next(0, 10) == 0) delay += Random.Shared.Next(200, 800);
            await Task.Delay(delay, ct);
        }
    }

    public async Task WarmupScrollAsync(IWebDriver driver, int durationSeconds = 30, CancellationToken ct = default)
    {
        if (!_config.HumanMode)
        {
            await Task.Delay(500, ct);
            return;
        }

        var end = DateTime.UtcNow.AddSeconds(durationSeconds);
        while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
        {
            var scrollAmount = Random.Shared.Next(200, 600);
            ((IJavaScriptExecutor)driver).ExecuteScript($"window.scrollBy(0, {scrollAmount})");
            await Task.Delay(Random.Shared.Next(1000, 3000), ct);

            if (Random.Shared.Next(0, 4) == 0)
            {
                ((IJavaScriptExecutor)driver).ExecuteScript(
                    $"window.scrollBy(0, -{Random.Shared.Next(50, 200)})");
                await Task.Delay(Random.Shared.Next(500, 1500), ct);
            }
        }
    }

    public bool IsSafeHour(string timezone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return !(localTime.Hour >= 3 && localTime.Hour < 7);
        }
        catch
        {
            return true;
        }
    }
}
