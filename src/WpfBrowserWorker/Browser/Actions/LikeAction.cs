using OpenQA.Selenium;
using Serilog;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class LikeAction : IAction
{
    private readonly HumanBehaviorSimulator _human;

    public string TaskType => "like";

    public LikeAction(HumanBehaviorSimulator human) => _human = human;

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        var log = new List<ActionLogStep>();

        if (string.IsNullOrEmpty(task.Target.Url))
            return TaskResult.Fail("Target URL is required for like action");

        await browser.NavigateAsync(task.Target.Url);
        log.Add(new ActionLogStep { Step = "navigate", Url = task.Target.Url });

        await _human.WarmupScrollAsync(browser.Driver, durationSeconds: 5, ct);

        // Instagram-specific selectors (extend for other platforms)
        var selectors = new[]
        {
            "button[aria-label='Like']",
            "span[aria-label='Like']",
            "//button[contains(@aria-label, 'Like')]"
        };

        IWebElement? likeButton = null;
        foreach (var selector in selectors)
        {
            try
            {
                likeButton = selector.StartsWith("//")
                    ? browser.Driver.FindElement(By.XPath(selector))
                    : browser.Driver.FindElement(By.CssSelector(selector));
                break;
            }
            catch (NoSuchElementException) { }
        }

        if (likeButton == null)
        {
            var failScreenshot = await browser.TakeScreenshotBase64Async();
            return TaskResult.Fail("Like button not found", failScreenshot);
        }

        log.Add(new ActionLogStep { Step = "find_like_button", Found = true });

        await _human.MoveAndClickAsync(browser.Driver, likeButton, ct);
        await _human.ActionDelayAsync(task.Config.HumanDelayMinMs, task.Config.HumanDelayMaxMs, ct);

        log.Add(new ActionLogStep { Step = "click", ActionSuccess = true });

        string? screenshot = null;
        if (task.Config.ScreenshotOnComplete)
            screenshot = await browser.TakeScreenshotBase64Async();

        var result = TaskResult.Succeed(log);
        result.ScreenshotBase64 = screenshot;
        return result;
    }
}
