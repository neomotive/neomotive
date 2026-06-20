using Avalonia.Controls;
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
}
