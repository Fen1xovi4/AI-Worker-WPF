using WpfBrowserWorker.Helpers;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Browser.Actions;

public class ScrollFeedAction : IAction
{
    private readonly HumanBehaviorSimulator _human;
    public string TaskType => "scroll_feed";

    public ScrollFeedAction(HumanBehaviorSimulator human) => _human = human;

    public async Task<TaskResult> ExecuteAsync(BrowserTask task, BrowserInstance browser, CancellationToken ct)
    {
        var duration = task.Target.DurationSeconds ?? 30;
        await _human.WarmupScrollAsync(browser.Driver, duration, ct);
        return TaskResult.Succeed();
    }
}
