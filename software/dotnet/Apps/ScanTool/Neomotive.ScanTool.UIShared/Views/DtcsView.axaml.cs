using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Neomotive.ScanTool.Core;

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

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = Vm.RefreshAsync();
    private void OnClearDtcs(object? sender, RoutedEventArgs e) => _ = Vm.ClearDtcsAsync();

    private void OnClearModuleDtcs(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ModuleDtcGroup group)
            _ = Vm.ClearModuleDtcsAsync(group.Module);
    }
}
