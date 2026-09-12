using Avalonia.Controls;
using Avalonia.Interactivity;
using Neomotive.ScanTool.Core;
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
        if (sender is Button { Tag: UdsModuleInfo module } && VM != null)
        {
            VM.SelectedUdsModule = module;
        }
    }

    private async void OnReadModuleDtcs(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ReadSelectedModuleDtcsAsync();
    }

    private async void OnClearModuleDtcs(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ClearSelectedModuleDtcsAsync();
    }

    private async void OnClearAllDtcs(object? sender, RoutedEventArgs e)
    {
        if (VM != null)
            await VM.ClearAllUdsDtcsAsync();
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
