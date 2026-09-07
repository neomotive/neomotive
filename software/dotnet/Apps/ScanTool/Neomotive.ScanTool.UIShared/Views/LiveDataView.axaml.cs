using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ScanTool.UI.Views;

public partial class LiveDataView : UserControl
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public LiveDataView() => InitializeComponent();

    private void OnOpenPicker(object? sender, RoutedEventArgs e)  => Vm.OpenLivePicker();
    private void OnSelectNone(object? sender, RoutedEventArgs e)  => Vm.SelectNoPids();
    private void OnShowTable(object? sender, RoutedEventArgs e)   => Vm.ShowTable();
    private void OnShowGauges(object? sender, RoutedEventArgs e)  => Vm.ShowGauges();
    private void OnShowWaveform(object? sender, RoutedEventArgs e)=> Vm.ShowWaveform();
    private void OnStartPolling(object? sender, RoutedEventArgs e)=> Vm.StartPolling();
    private void OnStopPolling(object? sender, RoutedEventArgs e) => Vm.StopPolling();
}
