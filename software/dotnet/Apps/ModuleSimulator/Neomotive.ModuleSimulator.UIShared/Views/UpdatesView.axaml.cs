using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ModuleSimulator.UI.Views;

public partial class UpdatesView : UserControl
{
    public UpdatesView() => InitializeComponent();

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private void OnCheckForUpdates(object? sender, RoutedEventArgs e) => _ = Vm.CheckForUpdatesAsync();
}
