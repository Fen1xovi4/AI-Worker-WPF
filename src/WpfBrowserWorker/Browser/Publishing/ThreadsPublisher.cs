using System.IO;
using OpenQA.Selenium;
using Serilog;
using WpfBrowserWorker.Helpers;

namespace WpfBrowserWorker.Browser.Publishing;

/// <summary>
/// Publishes a post to Threads via Selenium.
/// Assumes the browser is already logged in (profile-based session).
/// </summary>
public class ThreadsPublisher
{
    private readonly HumanBehaviorSimulator _human;

    public ThreadsPublisher(HumanBehaviorSimulator human)
    {
        _human = human;
    }

    public async Task<PublishResult> PublishAsync(
        IWebDriver driver, string text, byte[]? imageBytes, CancellationToken ct)
    {
        string? tempFile = null;
        try
        {
            // ── 1. Navigate to Threads ────────────────────────────────────────
            if (!driver.Url.Contains("threads.net") && !driver.Url.Contains("threads.com"))
            {
                driver.Navigate().GoToUrl("https://www.threads.net/");
                await WaitAsync(Random.Shared.Next(5000, 9000), ct);
            }

            // Handle login redirect
            if (driver.Url.Contains("login") || driver.Url.Contains("accounts/login"))
                return PublishResult.Fail("Not logged in. Please log in to Threads in this profile first.");

            // ── 1b. Warmup scroll ─────────────────────────────────────────────
            await _human.WarmupScrollAsync(driver, durationSeconds: Random.Shared.Next(8, 16), ct);
            await WaitAsync(Random.Shared.Next(2000, 4000), ct);

            // ── 2. Open compose dialog ────────────────────────────────────────
            await _human.ThinkAsync(ct);
            var composeBtn = FindElement(driver, TimeSpan.FromSeconds(15),
                "a[href*='/intent/']",
                "svg[aria-label='New thread']",
                "svg[aria-label='Новая цепочка']",
                "[aria-label*='Create']",
                "[aria-label*='Создать']");

            if (composeBtn is not null)
            {
                ClickParentOrSelf(driver, composeBtn);
                await WaitAsync(Random.Shared.Next(2000, 4000), ct);
            }
            else
            {
                // Threads sometimes shows a compose area inline at the top
                // Try clicking it directly
                var inlineCompose = FindElement(driver, TimeSpan.FromSeconds(5),
                    "div[role='button'][tabindex='0']",
                    "div[contenteditable='true']");

                if (inlineCompose is null)
                    return PublishResult.Fail("Compose button not found", await ScreenshotAsync(driver));

                await _human.MoveAndClickAsync(driver, inlineCompose, ct);
                await WaitAsync(1000, ct);
            }

            // ── 3. Find the text input ────────────────────────────────────────
            var textInput = FindElement(driver, TimeSpan.FromSeconds(10),
                "div[contenteditable='true']",
                "div[role='textbox']",
                "p[data-contents='true']");

            if (textInput is null)
                return PublishResult.Fail("Text input not found", await ScreenshotAsync(driver));

            await _human.MoveAndClickAsync(driver, textInput, ct);
            await WaitAsync(400, ct);

            // Type text via JS to handle contenteditable correctly
            await TypeIntoContentEditable(driver, textInput, text, ct);
            await WaitAsync(600, ct);

            // ── 4. Attach image (if any) ──────────────────────────────────────
            if (imageBytes is not null && imageBytes.Length > 0)
            {
                tempFile = SaveTempImage(imageBytes);

                // Threads hides input[type='file'] with CSS — skip Displayed check.
                // Try to find the hidden file input directly first.
                var fileInput = FindHiddenInput(driver, TimeSpan.FromSeconds(5), "input[type='file']");

                if (fileInput is null)
                {
                    // If no direct file input, look for an attachment icon button and click it
                    var attachBtn = FindElement(driver, TimeSpan.FromSeconds(5),
                        "svg[aria-label*='hoto']",
                        "svg[aria-label*='ото']",
                        "svg[aria-label*='ttach']",
                        "svg[aria-label*='mage']",
                        "[aria-label*='Add photo']",
                        "[aria-label*='Добавить фото']",
                        "[role='button'][aria-label*='photo' i]");

                    if (attachBtn is not null)
                    {
                        ClickParentOrSelf(driver, attachBtn);
                        await WaitAsync(1000, ct);
                        fileInput = FindHiddenInput(driver, TimeSpan.FromSeconds(8), "input[type='file']");
                    }
                }

                if (fileInput is not null)
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript(
                        "arguments[0].style.display='block'; arguments[0].style.opacity='1';", fileInput);
                    await WaitAsync(300, ct);
                    fileInput.SendKeys(tempFile);
                    await WaitAsync(Random.Shared.Next(4000, 7000), ct); // upload preview
                }
                else
                {
                    Log.Warning("Threads: file input not found — posting without image");
                }
            }

            // ── 5. Click "Post" button ────────────────────────────────────────
            await _human.ThinkAsync(ct);
            var posted = await ClickPostButtonAsync(driver, ct, timeoutSec: 12);

            if (!posted)
                return PublishResult.Fail("Post button not found", await ScreenshotAsync(driver));

            await WaitAsync(Random.Shared.Next(5000, 9000), ct);

            Log.Information("[OK] Threads: post published successfully");
            return PublishResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return PublishResult.Fail("Publishing cancelled");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Threads publish error");
            return PublishResult.Fail(ex.Message, await ScreenshotAsync(driver));
        }
        finally
        {
            if (tempFile is not null && File.Exists(tempFile))
                try { File.Delete(tempFile); } catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Multi-strategy search for the Threads "Post" submit button.
    /// Tries: aria-label CSS → text XPath → type=submit → last visible role=button.
    /// </summary>
    private async Task<bool> ClickPostButtonAsync(IWebDriver driver, CancellationToken ct, int timeoutSec)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSec);
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();

            // Strategy 1: aria-label (most reliable, language-independent)
            var ariaSelectors = new[]
            {
                "[aria-label='Post']",
                "[aria-label='Опубликовать']",
                "[aria-label='Поделиться']",
                "[aria-label='Share']",
                "[aria-label='Submit']",
            };
            foreach (var sel in ariaSelectors)
            {
                try
                {
                    var btn = driver.FindElements(By.CssSelector(sel)).FirstOrDefault(b => b.Displayed);
                    if (btn is not null) { await _human.MoveAndClickAsync(driver, btn, ct); return true; }
                }
                catch { }
            }

            // Strategy 2: XPath text match — //* covers both <button> and div[role=button]
            foreach (var label in new[] { "Post", "Опубликовать", "Поделиться", "Share" })
            {
                try
                {
                    var xpath = $"//*[self::button or @role='button'][contains(normalize-space(.), '{label}')]";
                    var btn = driver.FindElements(By.XPath(xpath)).FirstOrDefault(b => b.Displayed);
                    if (btn is not null) { await _human.MoveAndClickAsync(driver, btn, ct); return true; }
                }
                catch { }
            }

            // Strategy 3: type=submit button
            try
            {
                var btn = driver.FindElements(By.CssSelector("button[type='submit']"))
                               .FirstOrDefault(b => b.Displayed);
                if (btn is not null) { await _human.MoveAndClickAsync(driver, btn, ct); return true; }
            }
            catch { }

            // Strategy 4: Ctrl+Enter keyboard shortcut (works as submit in many web editors)
            try
            {
                var active = driver.FindElement(By.CssSelector("div[contenteditable='true'], div[role='textbox']"));
                if (active is not null && active.Displayed)
                {
                    new OpenQA.Selenium.Interactions.Actions(driver)
                        .MoveToElement(active)
                        .KeyDown(Keys.Control).SendKeys(Keys.Return).KeyUp(Keys.Control)
                        .Perform();
                    return true;
                }
            }
            catch { }

            await Task.Delay(600, ct);
        }
        return false;
    }

    // Threads hides input[type='file'] with CSS — skip Displayed check.
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
            Thread.Sleep(400);
        }
        return null;
    }

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
                    // XPath normalize-space handles nested spans inside buttons
                    var xpath = $"//*[@role='button' or self::button][contains(normalize-space(.), '{text}')]";
                    var candidates = driver.FindElements(By.XPath(xpath));
                    var btn = candidates.FirstOrDefault(b => b.Displayed);
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

    private async Task TypeIntoContentEditable(
        IWebDriver driver, IWebElement element, string text, CancellationToken ct)
    {
        // Use clipboard paste — handles emoji and all Unicode correctly
        await _human.PasteTextAsync(driver, element, text, ct);

        // Fire input event so React/Vue picks up the change
        ((IJavaScriptExecutor)driver).ExecuteScript(
            "arguments[0].dispatchEvent(new InputEvent('input', {bubbles:true, data: arguments[1]}));",
            element, text);
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

    private static string SaveTempImage(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"th_post_{Guid.NewGuid():N}.jpg");
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
