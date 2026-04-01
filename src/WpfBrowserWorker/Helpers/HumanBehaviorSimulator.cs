using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Helpers;

public class HumanBehaviorSimulator
{
    private readonly WorkerConfig _config;

    public HumanBehaviorSimulator(WorkerConfig config) => _config = config;

    public async Task MicroDelayAsync(CancellationToken ct = default)
    {
        if (!_config.HumanMode) return;
        await Task.Delay(Random.Shared.Next(600, 1800), ct);
    }

    public async Task ActionDelayAsync(int minMs, int maxMs, CancellationToken ct = default)
    {
        if (!_config.HumanMode) return;
        await Task.Delay(Random.Shared.Next(Math.Max(minMs, 100), Math.Max(maxMs, 200)), ct);
    }

    /// <summary>
    /// Pause before acting — simulates the user "thinking" before clicking.
    /// </summary>
    public async Task ThinkAsync(CancellationToken ct = default)
    {
        if (!_config.HumanMode) return;
        await Task.Delay(Random.Shared.Next(1200, 3500), ct);
    }

    public async Task MoveAndClickAsync(IWebDriver driver, IWebElement element, CancellationToken ct = default)
    {
        if (_config.HumanMode)
        {
            // "Think" before acting
            await Task.Delay(Random.Shared.Next(800, 2500), ct);

            var actions = new OpenQA.Selenium.Interactions.Actions(driver);
            var offsetX = Random.Shared.Next(-5, 5);
            var offsetY = Random.Shared.Next(-3, 3);
            actions.MoveToElement(element, offsetX, offsetY)
                   .Pause(TimeSpan.FromMilliseconds(Random.Shared.Next(300, 900)))
                   .Click()
                   .Perform();
        }
        else
        {
            element.Click();
        }

        await Task.Delay(Random.Shared.Next(200, 500), ct);
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
            // Base: 80–220 ms per character, realistic typing speed
            var delay = Random.Shared.Next(80, 220);
            // ~15% chance of a longer pause (thinking, correcting)
            if (Random.Shared.Next(0, 7) == 0) delay += Random.Shared.Next(400, 1200);
            // ~5% chance of a very long pause (distraction)
            if (Random.Shared.Next(0, 20) == 0) delay += Random.Shared.Next(1500, 3000);
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

    /// <summary>
    /// Pastes text into a focused element using the Windows clipboard.
    /// Handles emoji and all Unicode correctly — unlike SendKeys.
    /// Must be called after the element is already focused/clicked.
    /// </summary>
    public async Task PasteTextAsync(IWebDriver driver, IWebElement element, string text, CancellationToken ct = default)
    {
        // Set clipboard on the UI (STA) thread — required by Windows clipboard API
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(
            () => System.Windows.Clipboard.SetText(text),
            System.Windows.Threading.DispatcherPriority.Normal);

        await Task.Delay(Random.Shared.Next(200, 500), ct);

        // Select all existing content, then paste
        new Actions(driver).KeyDown(Keys.Control).SendKeys("a").KeyUp(Keys.Control).Perform();
        await Task.Delay(150, ct);
        new Actions(driver).KeyDown(Keys.Control).SendKeys("v").KeyUp(Keys.Control).Perform();
        await Task.Delay(Random.Shared.Next(300, 700), ct);
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
