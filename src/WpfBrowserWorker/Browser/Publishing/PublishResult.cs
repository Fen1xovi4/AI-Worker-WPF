namespace WpfBrowserWorker.Browser.Publishing;

public record PublishResult(bool Success, string? Error = null, string? ScreenshotBase64 = null)
{
    public static PublishResult Ok()                                           => new(true);
    public static PublishResult Fail(string error, string? screenshot = null) => new(false, error, screenshot);
}
