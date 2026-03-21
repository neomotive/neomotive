using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ModuleSimulator.UI.Views;

public partial class MonitorsView : UserControl
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public MonitorsView() => InitializeComponent();

    private void OnToggleMonitor(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
            Vm.ToggleMonitor(key);
    }
}
