using OpenQA.Selenium;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class FollowAction : IAction
{
    private readonly HumanBehaviorSimulator _human;
    public string TaskType => "follow";

    public FollowAction(HumanBehaviorSimulator human) => _human = human;

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        var url = task.Target.Url ?? $"https://www.instagram.com/{task.Target.Username}/";
        await browser.NavigateAsync(url);
        await _human.MicroDelayAsync(ct);

        // Check if already following
        try
        {
            browser.Driver.FindElement(By.XPath("//button[text()='Following']"));
            return TaskResult.Succeed();  // already following
        }
        catch (NoSuchElementException) { }

        IWebElement? followBtn = null;
        try { followBtn = browser.Driver.FindElement(By.XPath("//button[text()='Follow']")); }
        catch (NoSuchElementException) { }

        if (followBtn == null)
            return TaskResult.Fail("Follow button not found", await browser.TakeScreenshotBase64Async());

        await _human.MoveAndClickAsync(browser.Driver, followBtn, ct);
        await _human.ActionDelayAsync(task.Config.HumanDelayMinMs, task.Config.HumanDelayMaxMs, ct);

        return TaskResult.Succeed();
    }
}
