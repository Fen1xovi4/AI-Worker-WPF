using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WpfBrowserWorker.Browser;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly IBrowserManager _browserManager;
    private readonly WorkerStateService _state;

    public ObservableCollection<BrowserStatusItem> Accounts { get; } = new();

    public AccountsViewModel(IBrowserManager browserManager, WorkerStateService state)
    {
        _browserManager = browserManager;
        _state = state;
        _state.StateChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Accounts.Clear();
            foreach (var b in _browserManager.GetBrowserStatuses())
                Accounts.Add(b);
        });
    }
}
