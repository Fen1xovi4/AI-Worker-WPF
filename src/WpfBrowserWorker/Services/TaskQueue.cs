using System.Threading.Channels;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

/// <summary>
/// Единственная точка входа для заданий — не важно откуда пришло (API, UI).
/// Executor читает отсюда, не зная источника.
/// </summary>
public class TaskQueue
{
    private readonly Channel<BrowserTask> _channel =
        Channel.CreateUnbounded<BrowserTask>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

    public ValueTask EnqueueAsync(BrowserTask task) =>
        _channel.Writer.WriteAsync(task);

    public ValueTask<BrowserTask> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);

    public int Count => _channel.Reader.Count;
}
