using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Neomotive.ModuleSimulator.UI;

public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        DataContext = new MainWindowViewModel();
        InitializeComponent();
        Vm.CanLogUpdated += ScrollCanLogToBottom;
    }

    private void ScrollCanLogToBottom()
    {
        var scroller = this.FindControl<ScrollViewer>("CanLogScroller");
        if (scroller is null) return;
        Dispatcher.UIThread.Post(
            () => scroller.Offset = new Vector(scroller.Offset.X, double.MaxValue),
            DispatcherPriority.Background);
    }

    private void OnPcmPressed(object? sender, PointerPressedEventArgs e) => Vm.SelectPcm();
    private void OnTcuPressed(object? sender, PointerPressedEventArgs e) => Vm.SelectTcu();

    private void OnShowData(object? sender, RoutedEventArgs e)     => Vm.ShowData();
    private void OnShowMonitors(object? sender, RoutedEventArgs e) => Vm.ShowMonitors();
    private void OnShowDtcs(object? sender, RoutedEventArgs e)     => Vm.ShowDtcs();
    private void OnShowInputs(object? sender, RoutedEventArgs e)   => Vm.ShowInputs();
    private void OnShowConfig(object? sender, RoutedEventArgs e)   => Vm.ShowConfig();

    private void OnClearCanLog(object? sender, RoutedEventArgs e)  => Vm.ClearCanLog();

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)       { Vm.ExecuteCommand(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Vm.ClearCommand();   e.Handled = true; }
    }
}
