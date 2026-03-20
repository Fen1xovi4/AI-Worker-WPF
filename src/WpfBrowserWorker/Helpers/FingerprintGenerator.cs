using OpenQA.Selenium.Chrome;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Helpers;

public class FingerprintGenerator
{
    public void ApplyFingerprint(ChromeOptions options, BrowserFingerprint fingerprint, string userAgent)
    {
        var width = fingerprint.ScreenWidth + Random.Shared.Next(-10, 10);
        var height = fingerprint.ScreenHeight + Random.Shared.Next(-10, 10);
        options.AddArgument($"--window-size={width},{height}");
        options.AddArgument($"--lang={fingerprint.Language}");
        options.AddUserProfilePreference("intl.accept_languages", fingerprint.Language);
        options.AddArgument($"--user-agent={userAgent}");
    }

    public string GetStealthScript(BrowserFingerprint fingerprint) => $$"""
        Object.defineProperty(navigator, 'webdriver', {get: () => undefined});
        Object.defineProperty(navigator, 'platform', {get: () => '{{fingerprint.Platform}}'});
        Object.defineProperty(navigator, 'language', {get: () => '{{fingerprint.Language}}'});
        Object.defineProperty(navigator, 'languages', {get: () => ['{{fingerprint.Language}}', 'en']});
        """;
}
