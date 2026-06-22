using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Neomotive.ScanTool.UI.Views;

public partial class ScanToolView : UserControl
{
    public ScanToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private void OnShowConnection(object? sender, RoutedEventArgs e) => Vm.ShowConnection();
    private void OnShowVehicle(object? sender, RoutedEventArgs e)    => Vm.ShowVehicle();
    private void OnShowEmissions(object? sender, RoutedEventArgs e)  => Vm.ShowEmissions();
    private void OnShowDtcs(object? sender, RoutedEventArgs e)       => Vm.ShowDtcs();
    private void OnShowCanLog(object? sender, RoutedEventArgs e)      => Vm.ShowCanLog();
    private void OnShowLiveData(object? sender, RoutedEventArgs e)   => Vm.ShowLiveData();
    private void OnShowUpdates(object? sender, RoutedEventArgs e)    => Vm.ShowUpdates();
}
