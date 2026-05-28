using Meadow;
using Meadow.Foundation.Sensors;
using Meadow.Foundation.Sensors.Buttons;
using Meadow.Hardware;
using Meadow.Units;

public class MeadowApp : App<RaspberryPi>
{
    private SimulatorInputBoard _inputBoard = default!;

    public override Task Run()
    {
        Resolver.Log.Info("Input board test app");

        _inputBoard = new SimulatorInputBoard(Device);

        _inputBoard.Pot1.Changed += OnPotentiometerChanged;
        _inputBoard.Pot2.Changed += OnPotentiometerChanged;
        _inputBoard.Pot3.Changed += OnPotentiometerChanged;
        _inputBoard.Pot4.Changed += OnPotentiometerChanged;

        _inputBoard.Button1.LongClickedThreshold = TimeSpan.FromSeconds(5);
        _inputBoard.Button1.Clicked += OnButtonPressed;
        _inputBoard.Button2.Clicked += OnButtonPressed;
        _inputBoard.Button3.Clicked += OnButtonPressed;
        _inputBoard.Button4.Clicked += OnButtonPressed;

        _inputBoard.Button1.LongClicked += Button1_LongClicked;

        return Task.CompletedTask;
    }

    private void Button1_LongClicked(object? sender, EventArgs e)
    {
        Resolver.Log.Info($"Long press - shutdown requested");

        try
        {
            Device.Shutdown();
        }
        catch (Exception ex)
        {
            Resolver.Log.Info($"shutdown failed: {ex.Message}");
        }
    }

    private void OnPotentiometerChanged(object? sender, IChangeResult<Resistance> e)
    {
        var name = sender switch
        {
            Potentiometer pot when pot == _inputBoard.Pot1 => "Pot1",
            Potentiometer pot when pot == _inputBoard.Pot2 => "Pot2",
            Potentiometer pot when pot == _inputBoard.Pot3 => "Pot3",
            Potentiometer pot when pot == _inputBoard.Pot4 => "Pot4",
            _ => "Unknown Potentiometer"
        };
        Resolver.Log.Info($"{name} changed: {(sender as Potentiometer)!.GetCurrentPosition()} ({e.New.Ohms:N0} ohms)");
    }

    private void OnButtonPressed(object? sender, EventArgs e)
    {
        var name = sender switch
        {
            PushButton button when button == _inputBoard.Button1 => "Button 1",
            PushButton button when button == _inputBoard.Button2 => "Button 2",
            PushButton button when button == _inputBoard.Button3 => "Button 3",
            PushButton button when button == _inputBoard.Button4 => "Button 4",
            _ => "Unknown Button"
        };
        Resolver.Log.Info($"{name} clicked");
    }
}
