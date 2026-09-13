using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ScanTool.UI.Views;

public partial class CaptureBrowserView : UserControl
{
    private CaptureBrowserViewModel Vm => (CaptureBrowserViewModel)DataContext!;

    public CaptureBrowserView() => InitializeComponent();

    private void OnRefresh(object? sender, RoutedEventArgs e) => Vm.Refresh();

    private void OnClose(object? sender, RoutedEventArgs e) => Vm.Close();

    private void OnExportToUsb(object? sender, RoutedEventArgs e) => Vm.ExportToUsb();

    private void OnView(object? sender, RoutedEventArgs e) => Vm.View();
}
