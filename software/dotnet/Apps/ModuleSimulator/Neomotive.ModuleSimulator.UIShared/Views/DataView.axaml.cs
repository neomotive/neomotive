using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ModuleSimulator.UI.Views;

public partial class DataView : UserControl
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public DataView() => InitializeComponent();


}
