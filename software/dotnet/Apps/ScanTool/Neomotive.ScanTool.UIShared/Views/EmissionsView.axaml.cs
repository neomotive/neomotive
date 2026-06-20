using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Neomotive.ScanTool.UI.Views;

public partial class EmissionsView : UserControl
{
    public EmissionsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
