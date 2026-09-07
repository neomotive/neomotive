using Avalonia.Controls;
using Avalonia.Interactivity;
using Neomotive.ScanTool.Core.Diagnostics;

namespace Neomotive.ScanTool.UI.Views;

public partial class TriggerEditorView : UserControl
{
    private TriggerEditorViewModel Vm => (TriggerEditorViewModel)DataContext!;

    public TriggerEditorView() => InitializeComponent();

    private void OnModeManual(object? sender, RoutedEventArgs e) => Vm.Mode = ProfileTriggerMode.Manual;

    private void OnModeThreshold(object? sender, RoutedEventArgs e) => Vm.Mode = ProfileTriggerMode.Threshold;

    private void OnModeBusWake(object? sender, RoutedEventArgs e) => Vm.Mode = ProfileTriggerMode.BusWake;

    private void OnAbove(object? sender, RoutedEventArgs e) => Vm.Above = true;

    private void OnBelow(object? sender, RoutedEventArgs e) => Vm.Above = false;

    private void OnUseLive(object? sender, RoutedEventArgs e) => Vm.UseLiveValue();

    private void OnKey(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string digit })
        {
            Vm.KeypadAppend(digit);
        }
    }

    private void OnBackspace(object? sender, RoutedEventArgs e) => Vm.KeypadBackspace();

    private void OnClear(object? sender, RoutedEventArgs e) => Vm.KeypadClear();

    private void OnNegate(object? sender, RoutedEventArgs e) => Vm.KeypadNegate();

    private void OnCancel(object? sender, RoutedEventArgs e) => Vm.Cancel();

    private void OnConfirm(object? sender, RoutedEventArgs e) => Vm.Confirm();
}
