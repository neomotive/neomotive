using System.Runtime.InteropServices;
using Avalonia.Markup.Xaml;

namespace Neomotive.ScanTool.UI;

public partial class MainWindow : Avalonia.Controls.Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            CanResize = true;
            Width = 1024;
            Height = 614;
            MinWidth = 400;
            MinHeight = 240;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
