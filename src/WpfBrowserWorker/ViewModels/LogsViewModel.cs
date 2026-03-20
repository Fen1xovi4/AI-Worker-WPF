using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WpfBrowserWorker.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    public ObservableCollection<LogEntry> Entries { get; } = new();

    [ObservableProperty] private int _maxEntries = 500;

    public void AddEntry(LogEntry entry)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        });
    }

    [RelayCommand]
    private void Clear() => Entries.Clear();
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "INF";
    public string Message { get; set; } = string.Empty;
    public string LevelColor => Level switch
    {
        "ERR" => "#EF4444",
        "WRN" => "#F59E0B",
        "DBG" => "#9CA3AF",
        _ => "#374151"
    };
}
