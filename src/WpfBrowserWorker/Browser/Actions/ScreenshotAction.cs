using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class ScreenshotAction : IAction
{
    public string TaskType => "screenshot";

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(task.Target.Url))
            return TaskResult.Fail("URL is required for screenshot action");

        await browser.NavigateAsync(task.Target.Url);
        await Task.Delay(2000, ct);  // wait for full render

        var screenshot = await browser.TakeScreenshotBase64Async();
        if (screenshot == null)
            return TaskResult.Fail("Failed to take screenshot");

        var result = TaskResult.Succeed();
        result.ScreenshotBase64 = screenshot;
        return result;
    }
}
