using OpenQA.Selenium;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class CommentAction : IAction
{
    private readonly HumanBehaviorSimulator _human;
    public string TaskType => "comment";

    public CommentAction(HumanBehaviorSimulator human) => _human = human;

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(task.Target.Url) || string.IsNullOrEmpty(task.Target.Text))
            return TaskResult.Fail("URL and text are required for comment action");

        await browser.NavigateAsync(task.Target.Url);
        await _human.WarmupScrollAsync(browser.Driver, durationSeconds: 8, ct);

        IWebElement? commentInput = null;
        var selectors = new[] { "textarea[aria-label='Add a comment…']", "textarea[placeholder*='comment']" };
        foreach (var sel in selectors)
        {
            try { commentInput = browser.Driver.FindElement(By.CssSelector(sel)); break; }
            catch (NoSuchElementException) { }
        }

        if (commentInput == null)
            return TaskResult.Fail("Comment input not found", await browser.TakeScreenshotBase64Async());

        await _human.MoveAndClickAsync(browser.Driver, commentInput, ct);
        await _human.TypeAsync(commentInput, task.Target.Text, ct);
        await _human.MicroDelayAsync(ct);

        commentInput.SendKeys(Keys.Return);
        await _human.ActionDelayAsync(task.Config.HumanDelayMinMs, task.Config.HumanDelayMaxMs, ct);

        return TaskResult.Succeed();
    }
}
