using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ScanTool.UI.Views;

public partial class CaptureView : UserControl
{
    private CaptureViewModel Vm => (CaptureViewModel)DataContext!;

    public CaptureView() => InitializeComponent();

    private void OnApplyPreset(object? sender, RoutedEventArgs e) => Vm.ApplyDieselHardStartPreset();

    private void OnSelectAll(object? sender, RoutedEventArgs e) => Vm.SelectAll();

    private void OnSelectNone(object? sender, RoutedEventArgs e) => Vm.SelectNone();

    private void OnTogglePid(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CapturePidItem item })
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    private void OnToggleMode22(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Mode22SignalItem item })
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    private void OnArm(object? sender, RoutedEventArgs e) => Vm.Arm();

    private void OnTrigger(object? sender, RoutedEventArgs e) => Vm.TriggerNow();

    private void OnStop(object? sender, RoutedEventArgs e) => Vm.Stop();

    private void OnRefresh(object? sender, RoutedEventArgs e) => Vm.RefreshRecordings();

    private void OnExport(object? sender, RoutedEventArgs e) => Vm.ExportWideCsv();
}
