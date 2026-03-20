using OpenQA.Selenium;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class UnfollowAction : IAction
{
    private readonly HumanBehaviorSimulator _human;
    public string TaskType => "unfollow";

    public UnfollowAction(HumanBehaviorSimulator human) => _human = human;

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        var url = task.Target.Url ?? $"https://www.instagram.com/{task.Target.Username}/";
        await browser.NavigateAsync(url);
        await _human.MicroDelayAsync(ct);

        IWebElement? followingBtn = null;
        try { followingBtn = browser.Driver.FindElement(By.XPath("//button[text()='Following']")); }
        catch (NoSuchElementException) { return TaskResult.Succeed(); /* not following */ }

        await _human.MoveAndClickAsync(browser.Driver, followingBtn, ct);
        await _human.MicroDelayAsync(ct);

        // Confirm unfollow dialog
        try
        {
            var confirmBtn = browser.Driver.FindElement(By.XPath("//button[text()='Unfollow']"));
            await _human.MoveAndClickAsync(browser.Driver, confirmBtn, ct);
        }
        catch (NoSuchElementException) { }

        await _human.ActionDelayAsync(task.Config.HumanDelayMinMs, task.Config.HumanDelayMaxMs, ct);
        return TaskResult.Succeed();
    }
}
