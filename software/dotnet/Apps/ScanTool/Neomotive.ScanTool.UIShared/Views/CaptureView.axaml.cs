using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ScanTool.UI.Views;

public partial class CaptureView : UserControl
{
    private CaptureViewModel Vm => (CaptureViewModel)DataContext!;

    public CaptureView() => InitializeComponent();

    private void OnApplyProfile(object? sender, RoutedEventArgs e) => Vm.ApplySelectedProfile();

    private void OnOpenPicker(object? sender, RoutedEventArgs e) => Vm.OpenPicker();

    private void OnOpenTrigger(object? sender, RoutedEventArgs e) => Vm.OpenTriggerEditor();

    private void OnArm(object? sender, RoutedEventArgs e) => Vm.Arm();

    private void OnTrigger(object? sender, RoutedEventArgs e) => Vm.TriggerNow();

    private void OnStop(object? sender, RoutedEventArgs e) => Vm.Stop();

    private void OnRefresh(object? sender, RoutedEventArgs e) => Vm.RefreshRecordings();

    private void OnExport(object? sender, RoutedEventArgs e) => Vm.ExportWideCsv();
}
