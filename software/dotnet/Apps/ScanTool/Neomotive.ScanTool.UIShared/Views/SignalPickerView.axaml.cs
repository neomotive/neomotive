using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ScanTool.UI.Views;

public partial class SignalPickerView : UserControl
{
    private SignalPickerViewModel Vm => (SignalPickerViewModel)DataContext!;

    public SignalPickerView() => InitializeComponent();

    private void OnToggleSignal(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SignalItem item })
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    private void OnToggleSystem(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SystemFilterItem filter })
        {
            filter.IsActive = !filter.IsActive;
        }
    }

    private void OnToggleSelectedOnly(object? sender, RoutedEventArgs e)
        => Vm.ShowSelectedOnly = !Vm.ShowSelectedOnly;

    private void OnToggleSupportedOnly(object? sender, RoutedEventArgs e)
        => Vm.SupportedOnly = !Vm.SupportedOnly;

    private void OnSelectAllResults(object? sender, RoutedEventArgs e) => Vm.SelectAllResults();

    private void OnClearSelection(object? sender, RoutedEventArgs e) => Vm.ClearSelection();

    private void OnCancel(object? sender, RoutedEventArgs e) => Vm.Cancel();

    private void OnConfirm(object? sender, RoutedEventArgs e) => Vm.Confirm();
}
