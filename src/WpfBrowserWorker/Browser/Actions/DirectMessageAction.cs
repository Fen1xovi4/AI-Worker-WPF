using OpenQA.Selenium;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class DirectMessageAction : IAction
{
    private readonly HumanBehaviorSimulator _human;
    public string TaskType => "direct_message";

    public DirectMessageAction(HumanBehaviorSimulator human) => _human = human;

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(task.Target.Username) || string.IsNullOrEmpty(task.Target.Text))
            return TaskResult.Fail("Username and text are required for DM action");

        // Navigate to DM thread (Instagram)
        await browser.NavigateAsync($"https://www.instagram.com/{task.Target.Username}/");
        await _human.MicroDelayAsync(ct);

        try
        {
            var dmBtn = browser.Driver.FindElement(By.XPath("//button[text()='Message']"));
            await _human.MoveAndClickAsync(browser.Driver, dmBtn, ct);
            await Task.Delay(1500, ct);
        }
        catch (NoSuchElementException)
        {
            return TaskResult.Fail("Message button not found", await browser.TakeScreenshotBase64Async());
        }

        try
        {
            var input = browser.Driver.FindElement(By.CssSelector("div[role='textbox']"));
            await _human.MoveAndClickAsync(browser.Driver, input, ct);
            await _human.TypeAsync(input, task.Target.Text, ct);
            await _human.MicroDelayAsync(ct);
            input.SendKeys(Keys.Return);
        }
        catch (NoSuchElementException)
        {
            return TaskResult.Fail("DM input not found", await browser.TakeScreenshotBase64Async());
        }

        await _human.ActionDelayAsync(task.Config.HumanDelayMinMs, task.Config.HumanDelayMaxMs, ct);
        return TaskResult.Succeed();
    }
}
