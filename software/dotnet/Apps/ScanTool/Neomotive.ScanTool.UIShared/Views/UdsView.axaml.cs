using Avalonia.Controls;
using Avalonia.Interactivity;
using Meadow.Foundation.Telematics.Uds;

namespace Neomotive.ScanTool.UI.Views;

public partial class UdsView : UserControl
{
    public UdsView()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? VM => DataContext as MainWindowViewModel;

    private async void OnScanModules(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ScanUdsModulesAsync();
    }

    private async void OnProbeRemembered(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ProbeRememberedModulesAsync();
    }

    private void OnCancelScan(object? sender, RoutedEventArgs e) => VM?.CancelUdsScan();

    private void OnSelectModule(object? sender, RoutedEventArgs e)
    {
        // Goes through the view model rather than setting SelectedUdsModule directly, so the DTCs
        // page's selection follows along. Selecting a module here and finding a different one
        // selected on the DTCs page would be a quiet way to read the wrong module's codes.
        if (sender is Button { Tag: UdsModuleInfo module })
            VM?.SelectModuleByUds(module);
    }

    private async void OnReadVinDid(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ReadDidAsync(0xF190);
    }

    private async void OnReadPartDid(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ReadDidAsync(0xF187);
    }

    private async void OnReadSwDid(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ReadDidAsync(0xF189);
    }

    private async void OnReadCustomDid(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ReadCustomDidAsync();
    }
}
