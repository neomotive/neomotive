using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ScanTool.UI.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel Vm => (SettingsViewModel)DataContext!;

    public SettingsView() => InitializeComponent();

    // Stepping through a fixed set of periods rather than offering a slider: on a touch panel a
    // slider is a fiddly way to land on a round number, and the values worth picking are few.
    private void OnSlowerPoll(object? sender, RoutedEventArgs e) => Vm.StepLivePollPeriod(1);
    private void OnFasterPoll(object? sender, RoutedEventArgs e) => Vm.StepLivePollPeriod(-1);

    private void OnForgetAllVehicles(object? sender, RoutedEventArgs e) => Vm.ForgetAllVehicles();
}
