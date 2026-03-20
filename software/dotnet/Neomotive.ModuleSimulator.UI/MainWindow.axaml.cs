using Avalonia.Controls;
using Avalonia.Input;

namespace Neomotive.ModuleSimulator.UI;

public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        DataContext = new MainWindowViewModel();
        InitializeComponent();
    }

    private void OnPcmPressed(object? sender, PointerPressedEventArgs e) => Vm.SelectPcm();
    private void OnTcuPressed(object? sender, PointerPressedEventArgs e) => Vm.SelectTcu();

    private void OnShowData(object? sender, Avalonia.Interactivity.RoutedEventArgs e)     => Vm.ShowData();
    private void OnShowMonitors(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Vm.ShowMonitors();
    private void OnShowDtcs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)     => Vm.ShowDtcs();

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm.ExecuteCommand();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Vm.ClearCommand();
            e.Handled = true;
        }
    }
}
