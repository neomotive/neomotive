using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Neomotive.ScanTool.UI.Views;

public partial class VehicleView : UserControl
{
    public VehicleView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private void OnCheckTune(object? sender, RoutedEventArgs e) => _ = Vm.CheckTuneAsync();
}
