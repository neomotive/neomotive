using Meadow;
using Meadow.Foundation.Displays;
using Meadow.Foundation.Graphics;
using Meadow.Foundation.Graphics.MicroLayout;
using Meadow.Hardware;

namespace Neomotive.SignCamera;

public class DisplayService_1306 : IDisplayService
{
    private readonly DisplayScreen _screen;

    private ILayout _startupLayout;
    private ILayout _speedLayout;

    private Label _speedLabel;
    private Label _confidenceLabel;
    private Label _statusLabel;

    public DisplayService_1306(II2cBus i2cBus)
    {
        // this display is 128x64
        Resolver.Log.Info("Creating SSD1306 display...");

        var display = new Ssd1306(i2cBus);
        _screen = new DisplayScreen(display);

        CreateLayouts();
    }

    private void CreateLayouts()
    {
        _startupLayout = new AlignmentLayout(0, 0, _screen.Width, _screen.Height);

        _startupLayout.BackgroundColor = Color.Black;
        var titleLabel = new Label(_screen.Width, 20, "neomotive")
        {
            Font = new Font12x20(),
            TextColor = Color.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        ((AlignmentLayout)_startupLayout).Add(titleLabel, AlignmentLayout.DockPosition.Center);


        _speedLayout = new AlignmentLayout(0, 0, _screen.Width, _screen.Height);

        _speedLabel = new Label(96, 64, string.Empty)
        {
            Font = new Font16x24(),
            TextColor = Color.White,
        };

        _confidenceLabel = new Label(30, 30, string.Empty)
        {
            Font = new Font8x12(),
            TextColor = Color.White,
        };

        _statusLabel = new Label(30, 30, "*")
        {
            Font = new Font8x12(),
            TextColor = Color.White,
        };

        ((AlignmentLayout)_speedLayout).Add(_speedLabel, AlignmentLayout.DockPosition.Left);
        ((AlignmentLayout)_speedLayout).Add(_confidenceLabel, AlignmentLayout.DockPosition.Right);
        ((AlignmentLayout)_speedLayout).Add(_statusLabel, AlignmentLayout.DockPosition.TopRight);

        _screen.Controls.Add(_startupLayout, _speedLayout);

        //        _statusLabel.IsVisible = false;
    }

    public void ShowCaptureInProgress(bool show)
    {
        _statusLabel.IsVisible = show;
    }

    public async Task ShowStartup()
    {
        Resolver.Log.Info("Displaying startup screen...");

        _screen.BeginUpdate();
        _speedLayout.IsVisible = false;
        _startupLayout.IsVisible = true;
        _screen.EndUpdate();

        await Task.Delay(2000);

        _screen.BeginUpdate();
        _speedLabel.Text = "--";
        _confidenceLabel.Text = "--%";
        _speedLayout.IsVisible = true;
        _startupLayout.IsVisible = false;
        _screen.EndUpdate();

        Resolver.Log.Info("Done displaying startup screen...");
    }

    public async Task UpdateSpeedLimit(int speedLimit, double confidence)
    {
        Resolver.Log.Info("Displaying speed...");

        _startupLayout.IsVisible = false;

        _screen.BeginUpdate();
        _speedLayout.IsVisible = true;
        _confidenceLabel.Text = $"{(int)(confidence * 100)}%";
        _speedLabel.Text = $"{speedLimit}mph";

        _speedLayout.BackgroundColor = Color.White;
        _confidenceLabel.TextColor = Color.Black;
        _speedLabel.TextColor = Color.Black;
        _screen.EndUpdate();

        await Task.Delay(1000);

        _screen.BeginUpdate();
        _speedLayout.BackgroundColor = Color.Black;
        _speedLabel.TextColor = Color.White;
        _confidenceLabel.TextColor = Color.White;
        _screen.EndUpdate();
    }
}
