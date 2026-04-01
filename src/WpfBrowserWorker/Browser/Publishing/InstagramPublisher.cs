using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using Serilog;
using WpfBrowserWorker.Helpers;

namespace WpfBrowserWorker.Browser.Publishing;

/// <summary>
/// Publishes a post to Instagram via Selenium.
/// Assumes the browser is already logged in (profile-based session).
/// </summary>
public class InstagramPublisher
{
    private readonly HumanBehaviorSimulator _human;

    public InstagramPublisher(HumanBehaviorSimulator human)
    {
        _human = human;
    }

    public async Task<PublishResult> PublishAsync(
        IWebDriver driver, string text, byte[]? imageBytes, CancellationToken ct)
    {
        string? tempFile = null;
        try
        {
            // ── 1. Navigate to Instagram home ────────────────────────────────
            if (!driver.Url.Contains("instagram.com"))
            {
                driver.Navigate().GoToUrl("https://www.instagram.com/");
                await WaitAsync(Random.Shared.Next(5000, 9000), ct);
            }

            // Handle "Log in" redirect — means not logged in
            if (driver.Url.Contains("accounts/login"))
                return PublishResult.Fail("Not logged in. Please log in to Instagram in this profile first.");

            // ── 1b. Warmup: scroll the feed like a human ─────────────────────
            await _human.WarmupScrollAsync(driver, durationSeconds: Random.Shared.Next(12, 22), ct);
            await WaitAsync(Random.Shared.Next(2000, 4000), ct);

            // ── 2. Find and click the "New post" button in the navigation ─────
            await _human.ThinkAsync(ct);

            // Strategy A: CSS aria-label — known labels in various locales
            var newPostBtn = FindElement(driver, TimeSpan.FromSeconds(6),
                "[aria-label='New post']",        // EN
                "[aria-label='Nowy post']",        // PL
                "[aria-label='Create']",           // EN alt
                "[aria-label='Create post']",
                "[aria-label='Создать']",          // RU
                "[aria-label='Новая публикация']",
                "svg[aria-label='New post']",
                "svg[aria-label='Nowy post']",
                "svg[aria-label='Создать']",
                "a[href*='/create']");

            // Strategy B: JS — find by the distinctive "+" SVG path (language-independent)
            if (newPostBtn is null)
            {
                try
                {
                    newPostBtn = (IWebElement?)((IJavaScriptExecutor)driver).ExecuteScript(@"
                        // Instagram's create/new-post icon has this distinctive path fragment
                        for (const path of document.querySelectorAll('svg path')) {
                            const d = path.getAttribute('d') || '';
                            if (d.includes('M21 11h-8V3') || d.includes('M12 3a1 1')) {
                                const clickable = path.closest('a,[role=""link""],[role=""button""],div[tabindex=""0""]');
                                return clickable || path.closest('div');
                            }
                        }
                        return null;");
                }
                catch { }
            }

            // Strategy C: XPath — any SVG aria-label containing "post" (Nowy post, New post, …)
            if (newPostBtn is null)
            {
                try
                {
                    var xpath = "//*[self::svg or self::div][contains(translate(@aria-label," +
                                "'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'post')" +
                                " and not(contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'video'))" +
                                " and not(contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'live'))]";
                    newPostBtn = driver.FindElements(By.XPath(xpath)).FirstOrDefault(e => e.Displayed);
                }
                catch { }
            }

            if (newPostBtn is null)
            {
                try
                {
                    var labels = (string?)((IJavaScriptExecutor)driver).ExecuteScript(
                        "return Array.from(document.querySelectorAll('[aria-label]'))" +
                        ".slice(0, 50).map(e => e.tagName + ': ' + e.getAttribute('aria-label')).join(' || ')");
                    Log.Warning("Instagram: create button not found. Page aria-labels: {Labels}", labels ?? "(none)");
                }
                catch { }
                return PublishResult.Fail("New post button not found in nav", await ScreenshotAsync(driver));
            }

            // Walk up to the nearest clickable ancestor (a / button / role=button|link)
            // then use Actions click — more reliable than JS .click() on SVG elements
            var clickTarget = newPostBtn;
            try
            {
                var ancestor = (IWebElement?)((IJavaScriptExecutor)driver).ExecuteScript(@"
                    var el = arguments[0];
                    for (var i = 0; i < 8 && el && el !== document.body; i++) {
                        var tag  = el.tagName.toLowerCase();
                        var role = (el.getAttribute('role') || '').toLowerCase();
                        if (tag === 'a' || tag === 'button' || role === 'button' || role === 'link')
                            return el;
                        el = el.parentElement;
                    }
                    return arguments[0].parentElement || arguments[0];
                ", newPostBtn);
                if (ancestor is not null) clickTarget = ancestor;
            }
            catch { }

            await _human.MoveAndClickAsync(driver, clickTarget, ct);
            await WaitAsync(Random.Shared.Next(2000, 3500), ct);

            // ── 3. Click "Post" option if content-type picker appeared ─────────
            // Instagram shows a picker: Post / Reels / Live — click the Post option.
            // The text "Post" is the same in many locales; add known translations.
            var postOption = FindElementByText(driver, TimeSpan.FromSeconds(5),
                "span", "Post", "Публикация", "Beitrag", "Publication");
            if (postOption is not null)
            {
                await _human.MoveAndClickAsync(driver, postOption, ct);
                await WaitAsync(Random.Shared.Next(1500, 3000), ct);
            }

            // ── 4. Upload image ────────────────────────────────────────────────
            if (imageBytes is not null && imageBytes.Length > 0)
            {
                tempFile = SaveTempImage(imageBytes);

                // Instagram hides input[type='file'] with CSS — skip Displayed check.
                var fileInput = FindHiddenInput(driver, TimeSpan.FromSeconds(10),
                    "input[type='file']",
                    "input[accept*='image']",
                    "input[accept*='video']");

                if (fileInput is null)
                    return PublishResult.Fail("File upload input not found", await ScreenshotAsync(driver));

                ((IJavaScriptExecutor)driver).ExecuteScript(
                    "arguments[0].style.display='block'; arguments[0].style.opacity='1';", fileInput);
                await WaitAsync(300, ct);
                fileInput.SendKeys(tempFile);
                await WaitAsync(Random.Shared.Next(4000, 7000), ct);
            }

            // ── 5. Click "Next" / "Dalej" (crop step) ─────────────────────────
            await _human.ThinkAsync(ct);
            var ok = await ClickButtonByTextAsync(driver, ct, 15,
                "Next", "Далее", "Dalej", "Weiter", "Suivant", "Siguiente");
            if (!ok) return PublishResult.Fail("Next button (crop) not found", await ScreenshotAsync(driver));
            await WaitAsync(Random.Shared.Next(2500, 5000), ct);

            // ── 6. Click "Next" / "Dalej" (filter step) ───────────────────────
            await _human.ThinkAsync(ct);
            ok = await ClickButtonByTextAsync(driver, ct, 10,
                "Next", "Далее", "Dalej", "Weiter", "Suivant", "Siguiente");
            if (!ok) return PublishResult.Fail("Next button (filter) not found", await ScreenshotAsync(driver));
            await WaitAsync(Random.Shared.Next(2000, 4000), ct);

            // ── 7. Type caption ────────────────────────────────────────────────
            var caption = FindElement(driver, TimeSpan.FromSeconds(10),
                "div[data-lexical-editor='true']",           // most reliable, language-independent
                "div[aria-label='Write a caption...']",
                "div[aria-label='Введите подпись...']",
                "div[aria-label='Dodaj opis…']",             // PL
                "div[aria-label*='caption']",
                "div[aria-label*='opis']",
                "textarea[aria-label*='caption']");

            if (caption is null)
                return PublishResult.Fail("Caption input not found", await ScreenshotAsync(driver));

            await _human.MoveAndClickAsync(driver, caption, ct);
            await WaitAsync(400, ct);
            await _human.PasteTextAsync(driver, caption, text, ct);
            await WaitAsync(Random.Shared.Next(1500, 3000), ct);

            // ── 8. Click "Share" / "Udostępnij" ───────────────────────────────
            await _human.ThinkAsync(ct);
            ok = await ClickButtonByTextAsync(driver, ct, 10,
                "Share", "Поделиться", "Udostępnij", "Teilen", "Partager", "Compartir");
            if (!ok) return PublishResult.Fail("Share button not found", await ScreenshotAsync(driver));

            // Wait for upload to complete — Instagram shows a success screen inside the same modal.
            // URL may not change and the dialog stays open, so we just wait and treat Share click as success.
            await WaitAsync(Random.Shared.Next(6000, 10000), ct);

            // Best-effort check — if URL/modal changed quickly, great; otherwise assume success.
            await WaitForUrlChangeOrModalCloseAsync(driver, TimeSpan.FromSeconds(15));

            Log.Information("Instagram post published successfully");
            return PublishResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return PublishResult.Fail("Publishing cancelled");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Instagram publish error");
            return PublishResult.Fail(ex.Message, await ScreenshotAsync(driver));
        }
        finally
        {
            if (tempFile is not null && File.Exists(tempFile))
                try { File.Delete(tempFile); } catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IWebElement? FindElement(IWebDriver driver, TimeSpan timeout, params string[] cssSelectors)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            foreach (var sel in cssSelectors)
            {
                try
                {
                    var el = driver.FindElement(By.CssSelector(sel));
                    if (el.Displayed) return el;
                }
                catch { }
            }
            Thread.Sleep(500);
        }
        return null;
    }

    // File inputs on Instagram are CSS-hidden (opacity:0 / display:none).
    // This variant skips the Displayed check so hidden inputs are found.
    private static IWebElement? FindHiddenInput(IWebDriver driver, TimeSpan timeout, params string[] cssSelectors)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            foreach (var sel in cssSelectors)
            {
                try
                {
                    var els = driver.FindElements(By.CssSelector(sel));
                    if (els.Count > 0) return els[0]; // no Displayed check
                }
                catch { }
            }
            Thread.Sleep(500);
        }
        return null;
    }

    private static IWebElement? FindElementByText(IWebDriver driver, TimeSpan timeout,
        string tag, params string[] texts)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            foreach (var text in texts)
            {
                try
                {
                    var els = driver.FindElements(By.TagName(tag));
                    var found = els.FirstOrDefault(e =>
                        e.Displayed && e.Text.Equals(text, StringComparison.OrdinalIgnoreCase));
                    if (found is not null) return found;
                }
                catch { }
            }
            Thread.Sleep(400);
        }
        return null;
    }

    private async Task<bool> ClickButtonByTextAsync(
        IWebDriver driver, CancellationToken ct, int timeoutSec, params string[] texts)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSec);
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var text in texts)
            {
                try
                {
                    var buttons = driver.FindElements(By.CssSelector("button, div[role='button']"));
                    var btn = buttons.FirstOrDefault(b =>
                        b.Displayed && b.Text.Trim().Equals(text, StringComparison.OrdinalIgnoreCase));
                    if (btn is not null)
                    {
                        await _human.MoveAndClickAsync(driver, btn, ct);
                        return true;
                    }
                }
                catch { }
            }
            await Task.Delay(500, ct);
        }
        return false;
    }

    private static void ClickParentOrSelf(IWebDriver driver, IWebElement element)
    {
        var el = element;
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var tag  = el.TagName.ToLower();
                var role = el.GetDomAttribute("role") ?? string.Empty;
                if (tag is "a" or "button" || role == "button")
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", el);
                    return;
                }
                el = el.FindElement(By.XPath(".."));
            }
            catch { break; }
        }
        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", element);
    }

    private static async Task<bool> WaitForUrlChangeOrModalCloseAsync(IWebDriver driver, TimeSpan timeout)
    {
        var originalUrl = driver.Url;
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            // Either URL changed (navigated away) or the create modal disappeared
            if (driver.Url != originalUrl) return true;
            try
            {
                var modals = driver.FindElements(By.CssSelector("div[role='dialog']"));
                if (modals.Count == 0) return true;
            }
            catch { return true; }
            await Task.Delay(1000);
        }
        return false;
    }

    private static string SaveTempImage(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ig_post_{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Task<string?> ScreenshotAsync(IWebDriver driver)
    {
        try
        {
            var ss = ((ITakesScreenshot)driver).GetScreenshot();
            return Task.FromResult<string?>(ss.AsBase64EncodedString);
        }
        catch { return Task.FromResult<string?>(null); }
    }

    private static Task WaitAsync(int ms, CancellationToken ct) => Task.Delay(ms, ct);
}
