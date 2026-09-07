using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ModuleSimulator.UI.Views;

public partial class DataView : UserControl
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public DataView() => InitializeComponent();

    private void OnHealthyStart(object? sender, RoutedEventArgs e)
        => Vm.BeginStartAttempt(nameof(StartProfile.HealthyStart));

    private void OnWeakLiftPump(object? sender, RoutedEventArgs e)
        => Vm.BeginStartAttempt(nameof(StartProfile.WeakLiftPump));

    private void OnRailCollapse(object? sender, RoutedEventArgs e)
        => Vm.BeginStartAttempt(nameof(StartProfile.RailCollapse));

    private void OnWeakBattery(object? sender, RoutedEventArgs e)
        => Vm.BeginStartAttempt(nameof(StartProfile.WeakBattery));

    private void OnNoCrank(object? sender, RoutedEventArgs e)
        => Vm.BeginStartAttempt(nameof(StartProfile.NoCrank));

    private void OnStopAttempt(object? sender, RoutedEventArgs e)
        => Vm.StopStartAttempt();
}
