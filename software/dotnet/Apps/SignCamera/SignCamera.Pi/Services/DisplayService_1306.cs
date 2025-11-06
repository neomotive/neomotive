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

        ((AlignmentLayout)_speedLayout).Add(_speedLabel, AlignmentLayout.DockPosition.Left);
        ((AlignmentLayout)_speedLayout).Add(_confidenceLabel, AlignmentLayout.DockPosition.Right);

        _screen.Controls.Add(_startupLayout, _speedLayout);
    }

    /// <summary>
    /// Method to show startup screen and set visibility of layouts.
    /// </summary>
    /// <remarks>
    /// The method first sets the visibility of _speedLayout to false and _startupLayout to true, then delays for 2000 milliseconds.
    /// After that, it sets the text of _speedLabel and _confidenceLabel, sets the visibility of _speedLayout to true and _startupLayout to false.
    /// </remarks>
    /// <exception cref="System.Exception">Any exception that might occur during method execution.</exception>
    public async Task ShowStartup()
    {
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
    }

    /// <summary>
    /// Updates the speed limit and displays it on the screen.
    /// </summary>
    /// <param name="speedLimit">The new speed limit in mph.</param>
    /// <param name="confidence">The confidence level for the update, as a percentage (0-100).</param>
    /// <remarks>The method sets the visibility of certain layout elements and updates their text color. It also adjusts the background color of some layout elements before and after a delay.</remarks>
    public async Task UpdateSpeedLimit(int speedLimit, double confidence)
    {
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
