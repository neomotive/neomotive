using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Neomotive.ScanTool.UI.Views;

public partial class CaptureView : UserControl
{
    private CaptureViewModel Vm => (CaptureViewModel)DataContext!;

    /// <summary>
    /// The one and only review pane. Moved between the inline host and the full-screen host rather
    /// than being declared in both: a second instance would carry its own zoom, pan, cursor and
    /// lane-toggle state, so expanding or closing would quietly reset the operator's view of the
    /// trace — exactly the thing expanding is meant to help them look at.
    /// </summary>
    private readonly CaptureReviewPane _reviewPane = new();

    public CaptureView()
    {
        InitializeComponent();

        _reviewPane.DataContext = DataContext;
        DataContextChanged += (_, _) => _reviewPane.DataContext = DataContext;

        AttachReviewPane(expanded: false);
    }

    private void AttachReviewPane(bool expanded)
    {
        var inline = this.FindControl<ContentControl>("InlineReviewHost");
        var full = this.FindControl<ContentControl>("ExpandedReviewHost");

        // Detach from both before attaching, or Avalonia throws on a control that already has a
        // logical parent.
        if (inline is not null) inline.Content = null;
        if (full is not null) full.Content = null;

        if (expanded)
        {
            if (full is not null) full.Content = _reviewPane;
        }
        else if (inline is not null)
        {
            inline.Content = _reviewPane;
        }
    }

    private void OnApplyProfile(object? sender, RoutedEventArgs e) => Vm.ApplySelectedProfile();

    private void OnOpenPicker(object? sender, RoutedEventArgs e) => Vm.OpenPicker();

    private void OnOpenTrigger(object? sender, RoutedEventArgs e) => Vm.OpenTriggerEditor();

    private void OnArm(object? sender, RoutedEventArgs e)
    {
        Vm.Arm();

        // Arming is the moment the operator stops setting things up and starts watching, so the
        // view follows them there instead of leaving them on a page of controls.
        Vm.ShowReviewTab();
    }

    private void OnTrigger(object? sender, RoutedEventArgs e) => Vm.TriggerNow();

    private void OnStop(object? sender, RoutedEventArgs e) => Vm.Stop();

    private void OnRefresh(object? sender, RoutedEventArgs e) => Vm.RefreshRecordings();

    private void OnExport(object? sender, RoutedEventArgs e) => Vm.ExportWideCsv();

    private void OnShowConfigure(object? sender, RoutedEventArgs e) => Vm.ShowConfigureTab();

    private void OnShowReview(object? sender, RoutedEventArgs e) => Vm.ShowReviewTab();

    private void OnExpandReview(object? sender, RoutedEventArgs e)
    {
        Vm.IsReviewExpanded = true;
        AttachReviewPane(expanded: true);
    }

    private void OnCollapseReview(object? sender, RoutedEventArgs e)
    {
        Vm.IsReviewExpanded = false;
        AttachReviewPane(expanded: false);
    }
}
