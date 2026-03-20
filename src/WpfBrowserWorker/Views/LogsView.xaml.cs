using System.Collections.Specialized;
using System.Windows.Controls;
using WpfBrowserWorker.ViewModels;

namespace WpfBrowserWorker.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LogsViewModel vm)
                vm.Entries.CollectionChanged += AutoScroll;
        };
    }

    private void AutoScroll(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
