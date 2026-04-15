using Avalonia.Controls;

namespace Neomotive.ModuleSimulator.UI;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
