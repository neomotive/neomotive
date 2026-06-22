using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ModuleSimulator.UI.Views;

public partial class ConfigView : UserControl
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public ConfigView() => InitializeComponent();

    private void OnSetImperial(object? sender, RoutedEventArgs e) => Vm.SetUnits(UnitsOfMeasure.Imperial);
    private void OnSetMetric(object? sender, RoutedEventArgs e)   => Vm.SetUnits(UnitsOfMeasure.Metric);

    private void OnAddQuickDtc(object? sender, RoutedEventArgs e) => Vm.AddQuickDtc();
    private void OnCheckForUpdates(object? sender, RoutedEventArgs e) => _ = Vm.CheckForUpdatesAsync();

    private void OnRemoveQuickDtc(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string code })
            Vm.RemoveQuickDtc(code);
    }
}
