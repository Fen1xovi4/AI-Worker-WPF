using Serilog.Core;
using Serilog.Events;
using WpfBrowserWorker.ViewModels;

namespace WpfBrowserWorker.Helpers;

/// <summary>
/// Serilog sink that forwards log events to LogsViewModel for display in the UI.
/// Uses a static Instance so it can be created before the DI container is built,
/// then wired up to the ViewModel afterwards.
/// </summary>
public class InMemoryLogSink : ILogEventSink
{
    public static readonly InMemoryLogSink Instance = new();

    private Action<LogEntry>? _onEntry;

    public void Attach(Action<LogEntry> callback) => _onEntry = callback;

    public void Emit(LogEvent logEvent)
    {
        // Skip EF Core infrastructure noise (SQL queries, DbCommand traces, migrations)
        if (logEvent.Properties.TryGetValue("SourceContext", out var ctx))
        {
            var src = ctx.ToString().Trim('"');
            if (src.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
                return;
        }

        var level = logEvent.Level switch
        {
            LogEventLevel.Error       => "ERR",
            LogEventLevel.Fatal       => "ERR",
            LogEventLevel.Warning     => "WRN",
            LogEventLevel.Debug       => "DBG",
            LogEventLevel.Verbose     => "DBG",
            _                         => "INF"
        };

        var message = logEvent.RenderMessage();
        if (logEvent.Exception is { } ex)
            message += $" | {ex.GetType().Name}: {ex.Message}";

        _onEntry?.Invoke(new LogEntry
        {
            Timestamp = logEvent.Timestamp.LocalDateTime,
            Level     = level,
            Message   = message
        });
    }
}
