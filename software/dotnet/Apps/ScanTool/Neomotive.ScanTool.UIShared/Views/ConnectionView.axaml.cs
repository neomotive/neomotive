using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Neomotive.ScanTool.UI.Views;

public partial class ConnectionView : UserControl
{
    public ConnectionView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private void OnConnect(object? sender, RoutedEventArgs e)    => _ = Vm.ConnectAsync();
    private void OnDisconnect(object? sender, RoutedEventArgs e) => Vm.Disconnect();
}
