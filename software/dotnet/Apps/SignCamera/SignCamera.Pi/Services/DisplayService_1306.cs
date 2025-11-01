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

    public DisplayService_1306(II2cBus i2cBus)
    {
        // this display is 128x64
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

        _speedLabel = new Label(96, 64, "--")
        {
            Font = new Font16x24(),
            TextColor = Color.White,
            BackgroundColor = Color.Black
        };

        _confidenceLabel = new Label(30, 30, "--%")
        {
            Font = new Font8x12(),
            //            HorizontalAlignment = HorizontalAlignment.Right,
            TextColor = Color.White,
            BackgroundColor = Color.Black
        };

        ((AlignmentLayout)_speedLayout).Add(_speedLabel, AlignmentLayout.DockPosition.Left);
        ((AlignmentLayout)_speedLayout).Add(_confidenceLabel, AlignmentLayout.DockPosition.Right);

        _screen.Controls.Add(_startupLayout, _speedLayout);
    }

    public void ShowStartup()
    {
        _speedLayout.IsVisible = false;
        _startupLayout.IsVisible = true;
    }

    public void UpdateSpeedLimit(int speedLimit, double confidence)
    {
        _startupLayout.IsVisible = false;
        _speedLabel.Text = speedLimit.ToString();
        _confidenceLabel.Text = $"{(int)(confidence * 100)}%";
        _speedLayout.IsVisible = true;
    }
}
