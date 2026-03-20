using OpenQA.Selenium;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class AnalyzeProfileAction : IAction
{
    public string TaskType => "analyze_profile";

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        var url = task.Target.Url ?? $"https://www.instagram.com/{task.Target.Username}/";
        await browser.NavigateAsync(url);
        await Task.Delay(2000, ct);

        var data = new Dictionary<string, object>();

        TryExtract(browser.Driver, "meta[property='og:description']", "content", "description", data);
        TryExtract(browser.Driver, "meta[property='og:title']", "content", "title", data);

        var result = TaskResult.Succeed();
        result.ExtractedData = data;
        return result;
    }

    private static void TryExtract(IWebDriver driver, string selector,
        string attribute, string key, Dictionary<string, object> data)
    {
        try
        {
            var el = driver.FindElement(By.CssSelector(selector));
            var value = el.GetAttribute(attribute);
            if (!string.IsNullOrEmpty(value)) data[key] = value;
        }
        catch (NoSuchElementException) { }
    }
}
