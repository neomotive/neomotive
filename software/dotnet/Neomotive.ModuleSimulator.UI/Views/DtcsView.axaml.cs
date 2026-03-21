using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ModuleSimulator.UI.Views;

public partial class DtcsView : UserControl
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public DtcsView() => InitializeComponent();

    private void OnToggleQuickDtc(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string code })
            Vm.ToggleKnownDtc(code);
    }

    private void OnClearDtc(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string code })
            Vm.ClearDtc(code);
    }

    private void OnRemoveFromList(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string code })
            Vm.RemoveDtcFromList(code);
    }

    private void OnDeactivate(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string code })
            Vm.DeactivateDtc(code);
    }

    private void OnActivate(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string code })
            Vm.ActivateDtc(code);
    }

    private void OnClearAll(object? sender, RoutedEventArgs e) => Vm.ClearAllDtcs();
}
