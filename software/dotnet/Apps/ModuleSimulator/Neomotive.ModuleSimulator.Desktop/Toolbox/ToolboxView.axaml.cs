using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Neomotive.ModuleSimulator.UI.Toolbox;

public partial class ToolboxView : UserControl
{
    private ToolboxViewModel? Vm => DataContext as ToolboxViewModel;

    public ToolboxView()
    {
        InitializeComponent();
    }

    private void OnButton1Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not null) Vm.Button1Down = true;
        if (sender is Border b) b.Background = new SolidColorBrush(Color.Parse("#4A5568"));
    }

    private void OnButton1Released(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm is not null) Vm.Button1Down = false;
        if (sender is Border b) b.Background = new SolidColorBrush(Color.Parse("#2E3440"));
    }

    private void OnButton2Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not null) Vm.Button2Down = true;
        if (sender is Border b) b.Background = new SolidColorBrush(Color.Parse("#4A5568"));
    }

    private void OnButton2Released(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm is not null) Vm.Button2Down = false;
        if (sender is Border b) b.Background = new SolidColorBrush(Color.Parse("#2E3440"));
    }

    private void OnButton3Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not null) Vm.Button3Down = true;
        if (sender is Border b) b.Background = new SolidColorBrush(Color.Parse("#4A5568"));
    }

    private void OnButton3Released(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm is not null) Vm.Button3Down = false;
        if (sender is Border b) b.Background = new SolidColorBrush(Color.Parse("#2E3440"));
    }

    private void OnButton4Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not null) Vm.Button4Down = true;
        if (sender is Border b) b.Background = new SolidColorBrush(Color.Parse("#4A5568"));
    }

    private void OnButton4Released(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm is not null) Vm.Button4Down = false;
        if (sender is Border b) b.Background = new SolidColorBrush(Color.Parse("#2E3440"));
    }
}
