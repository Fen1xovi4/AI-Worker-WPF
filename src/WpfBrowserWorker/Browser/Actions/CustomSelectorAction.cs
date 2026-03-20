using OpenQA.Selenium;
using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class CustomSelectorAction : IAction
{
    private readonly HumanBehaviorSimulator _human;
    public string TaskType => "custom";

    public CustomSelectorAction(HumanBehaviorSimulator human) => _human = human;

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(task.Target.Url) || string.IsNullOrEmpty(task.Target.Selector))
            return TaskResult.Fail("URL and selector are required for custom action");

        await browser.NavigateAsync(task.Target.Url);
        await Task.Delay(1500, ct);

        IWebElement element;
        try
        {
            element = browser.Driver.FindElement(By.CssSelector(task.Target.Selector));
        }
        catch (NoSuchElementException)
        {
            return TaskResult.Fail($"Selector not found: {task.Target.Selector}",
                await browser.TakeScreenshotBase64Async());
        }

        var action = task.Target.Action?.ToLower() ?? "click";
        var extractedData = new Dictionary<string, object>();

        switch (action)
        {
            case "click":
                await _human.MoveAndClickAsync(browser.Driver, element, ct);
                break;

            case "type":
                if (string.IsNullOrEmpty(task.Target.Text))
                    return TaskResult.Fail("Text is required for type action");
                await _human.TypeAsync(element, task.Target.Text, ct);
                break;

            case "extract":
                extractedData["text"] = element.Text;
                extractedData["value"] = element.GetAttribute("value") ?? string.Empty;
                extractedData["href"] = element.GetAttribute("href") ?? string.Empty;
                break;

            default:
                return TaskResult.Fail($"Unknown action: {action}");
        }

        await _human.ActionDelayAsync(task.Config.HumanDelayMinMs, task.Config.HumanDelayMaxMs, ct);

        var result = TaskResult.Succeed();
        if (extractedData.Count > 0) result.ExtractedData = extractedData;
        return result;
    }
}
