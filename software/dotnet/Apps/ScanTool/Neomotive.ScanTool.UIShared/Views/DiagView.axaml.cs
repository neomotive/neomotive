using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Neomotive.ScanTool.UI.Views;

public partial class DiagView : UserControl
{
    public DiagView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnShowCan(object? sender, RoutedEventArgs e) => Vm?.ShowDiagCan();
    private void OnShowUds(object? sender, RoutedEventArgs e) => Vm?.ShowDiagUds();
}
