using Avalonia.Controls;
using Neomotive.ModuleSimulator.UI.Toolbox;

namespace Neomotive.ModuleSimulator.UI;

public partial class DesktopShellWindow : Window
{
    public DesktopShellWindow(MainWindowViewModel mainVm, ToolboxViewModel toolboxVm)
    {
        InitializeComponent();
        // Set each panel's DataContext explicitly after init so compiled bindings
        // in ToolboxView never see MainWindowViewModel as their DataContext.
        SimulatorPanel.DataContext = mainVm;
        ToolboxPanel.DataContext = toolboxVm;
    }
}
