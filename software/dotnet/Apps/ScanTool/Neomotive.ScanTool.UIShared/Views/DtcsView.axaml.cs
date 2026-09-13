using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Neomotive.ScanTool.Core;

namespace Neomotive.ScanTool.UI.Views;

public partial class DtcsView : UserControl
{
    public DtcsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnSelectModule(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FaultModule module } && Vm != null)
            Vm.SelectedFaultModule = module;
    }

    private async void OnScanModules(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ScanUdsModulesAsync();
    }

    private async void OnProbeRemembered(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ProbeRememberedModulesAsync();
    }

    private void OnCancelScan(object? sender, RoutedEventArgs e) => Vm?.CancelUdsScan();

    private async void OnReadModuleDtcs(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ReadSelectedFaultModuleAsync();
    }

    private async void OnClearModuleDtcs(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ClearSelectedFaultModuleAsync();
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.RefreshAsync();
    }

    private async void OnClearDtcs(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ClearAllFaultsAsync();
    }
}
