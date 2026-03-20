using OpenQA.Selenium;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class ViewStoryAction : IAction
{
    private readonly HumanBehaviorSimulator _human;
    public string TaskType => "view_story";

    public ViewStoryAction(HumanBehaviorSimulator human) => _human = human;

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        var username = task.Target.Username ?? throw new ArgumentException("Username required for view_story");
        var url = $"https://www.instagram.com/stories/{username}/";

        await browser.NavigateAsync(url);
        await Task.Delay(2000, ct);

        // Advance through stories by clicking the right side
        for (int i = 0; i < 10 && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var nextBtn = browser.Driver.FindElement(By.CssSelector("button[aria-label='Next']"));
                await _human.MoveAndClickAsync(browser.Driver, nextBtn, ct);
                await Task.Delay(Random.Shared.Next(2000, 5000), ct);
            }
            catch (NoSuchElementException)
            {
                break;  // no more stories
            }
        }

        return TaskResult.Succeed();
    }
}
