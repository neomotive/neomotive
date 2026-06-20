using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Neomotive.ScanTool.UI.Views;

public partial class CanView : UserControl
{
    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    public CanView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CanLogUpdated += ScrollCanLogToBottom;
        }
    }

    private void ScrollCanLogToBottom()
    {
        var scroller = this.FindControl<ScrollViewer>("CanLogScroller");
        if (scroller is null) return;
        Dispatcher.UIThread.Post(
            () => scroller.Offset = new Vector(scroller.Offset.X, double.MaxValue),
            DispatcherPriority.Background);
    }

    private void OnClearCanLog(object? sender, RoutedEventArgs e) => Vm?.ClearCanLog();
}
