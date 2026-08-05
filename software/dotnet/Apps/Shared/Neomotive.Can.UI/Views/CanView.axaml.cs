using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Neomotive.Can.UI.Views;

public partial class CanView : UserControl
{
    private ICanViewModel? Vm => DataContext as ICanViewModel;

    public CanView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ICanViewModel vm)
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

    private void OnResetCanErrors(object? sender, RoutedEventArgs e) => Vm?.ResetCanErrors();
    private void OnClearCanLog(object? sender, RoutedEventArgs e) => Vm?.ClearCanLog();
}
