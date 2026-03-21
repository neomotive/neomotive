using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ModuleSimulator.UI.Views;

public partial class ConfigView : UserControl
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public ConfigView() => InitializeComponent();

    private void OnAddQuickDtc(object? sender, RoutedEventArgs e) => Vm.AddQuickDtc();

    private void OnRemoveQuickDtc(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string code })
            Vm.RemoveQuickDtc(code);
    }
}
