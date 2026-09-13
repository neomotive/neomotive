using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ScanTool.UI.Views;

public partial class NoVehicleDialog : UserControl
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public NoVehicleDialog() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Vm.DismissNoVehicleHelp();

    private void OnRetry(object? sender, RoutedEventArgs e)
    {
        Vm.DismissNoVehicleHelp();
        _ = Vm.ConnectAsync();
    }
}
