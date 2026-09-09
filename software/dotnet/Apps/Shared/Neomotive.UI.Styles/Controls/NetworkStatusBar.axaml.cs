using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Neomotive.UI.Controls;

/// <summary>
/// A one-line network summary: connection icon with state colour, hostname, and
/// the IPv4 address when one is assigned. Supplies its own view model, so hosts
/// only need to place the control — no bindings or plumbing required.
/// </summary>
public partial class NetworkStatusBar : UserControl
{
    private readonly NetworkStatusViewModel _vm = new();

    public NetworkStatusBar()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm.Stop();
    }
}
