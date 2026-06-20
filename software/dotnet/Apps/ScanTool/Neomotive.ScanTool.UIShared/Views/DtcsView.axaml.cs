using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Neomotive.ScanTool.UI.Views;

public partial class DtcsView : UserControl
{
    public DtcsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private void OnClearDtcs(object? sender, RoutedEventArgs e) => _ = Vm.ClearDtcsAsync();
}
