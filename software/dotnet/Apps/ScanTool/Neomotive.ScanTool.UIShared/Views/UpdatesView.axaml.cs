using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Neomotive.ScanTool.UI.Views;

public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private void OnCheckForUpdates(object? sender, RoutedEventArgs e) => _ = Vm.CheckForUpdatesAsync();
}
