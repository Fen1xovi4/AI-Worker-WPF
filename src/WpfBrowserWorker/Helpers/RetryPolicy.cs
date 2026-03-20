using System.Net.Http;
using Serilog;

namespace WpfBrowserWorker.Helpers;

public class RetryPolicy
{
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        int maxAttempts = 4,
        CancellationToken ct = default)
    {
        var delay = TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (
                (int)(ex.StatusCode ?? 0) >= 500 && attempt < maxAttempts)
            {
                Log.Warning("HTTP {StatusCode} on attempt {Attempt}/{Max}, retrying in {Delay}s",
                    ex.StatusCode, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < maxAttempts)
            {
                Log.Warning("Request timeout on attempt {Attempt}/{Max}", attempt, maxAttempts);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        // Final attempt without catch
        return await operation();
    }
}
