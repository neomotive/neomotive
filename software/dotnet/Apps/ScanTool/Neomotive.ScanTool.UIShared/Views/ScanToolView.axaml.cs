using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Neomotive.Update;

namespace Neomotive.ScanTool.UI.Views;

public partial class ScanToolView : UserControl
{
    public ScanToolView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private void OnShowConnection(object? sender, RoutedEventArgs e) => Vm.ShowConnection();
    private void OnShowVehicle(object? sender, RoutedEventArgs e)    => Vm.ShowVehicle();
    private void OnShowEmissions(object? sender, RoutedEventArgs e)  => Vm.ShowEmissions();
    private void OnShowDtcs(object? sender, RoutedEventArgs e)       => Vm.ShowDtcs();
    private void OnShowDiag(object? sender, RoutedEventArgs e)       => Vm.ShowDiag();
    private void OnShowLiveData(object? sender, RoutedEventArgs e)   => Vm.ShowLiveData();
    private void OnShowCapture(object? sender, RoutedEventArgs e)    => Vm.ShowCapture();
    private void OnShowUpdates(object? sender, RoutedEventArgs e)    => Vm.ShowUpdates();
    private void OnShowSettings(object? sender, RoutedEventArgs e)   => Vm.ShowSettings();

    private void OnScreenCapture(object? sender, RoutedEventArgs e) => PerformScreenCapture();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.PrintScreen || e.Key == Key.F12)
        {
            PerformScreenCapture();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Renders the entire view to a PNG image, saves to the app's data directory and
    /// any connected USB drive, and triggers a brief toast notification.
    /// </summary>
    public void PerformScreenCapture()
    {
        if (DataContext is null) return;

        try
        {
            var dataDir = !string.IsNullOrWhiteSpace(Vm.DataDirectory)
                ? Vm.DataDirectory
                : Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dataDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = $"screenshot-{stamp}.png";
            var filePath = Path.Combine(dataDir, fileName);

            var width = (int)Math.Max(800, Bounds.Width > 0 ? Bounds.Width : 800);
            var height = (int)Math.Max(480, Bounds.Height > 0 ? Bounds.Height : 480);
            var pixelSize = new PixelSize(width, height);

            using (var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96)))
            {
                rtb.Render(this);
                rtb.Save(filePath);
            }

            var usbRoot = UsbUpdateSource.GetRemovableRoots().FirstOrDefault();
            var usbNote = "";
            if (!string.IsNullOrWhiteSpace(usbRoot) && Directory.Exists(usbRoot))
            {
                try
                {
                    var targetDir = Path.Combine(usbRoot, "NEOMOTIVE", "screenshots");
                    Directory.CreateDirectory(targetDir);
                    File.Copy(filePath, Path.Combine(targetDir, fileName), overwrite: true);
                    usbNote = " (+ USB)";
                }
                catch
                {
                    // USB export failure must not fail local capture.
                }
            }

            Vm.TriggerToast($"Screen captured: {fileName}{usbNote}");
            Vm.CaptureVm?.Browser?.Refresh();
        }
        catch (Exception ex)
        {
            Vm.TriggerToast($"Capture failed: {ex.Message}");
        }
    }
}
