using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Meadow;
using Meadow.Avalonia;
using Meadow.Foundation.ICs.CAN;
using Meadow.Hardware;
using Meadow.Logging;
using Meadow.Units;
using Neomotive.ModuleSimulator.UI;
using System;
using System.Threading.Tasks;

namespace Neomotive.ModuleSimulator.RaspberryPi;

public partial class App : AvaloniaMeadowApplication<Meadow.RaspberryPi>
{
    private readonly TaskCompletionSource<(ICanBus bus, string feedback)> _busReady = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = LoadMeadowOS();
    }

    public override Task MeadowInitialize()
    {
        Console.WriteLine("Initializing Meadow application...");

        Resolver.Log.AddProvider(new ConsoleLogProvider());
        Resolver.Log.AddProvider(new Meadow.Logging.UdpLogger());

        try
        {
            Resolver.Log.Info("Initializing CAN bus...");
            var pins = Device.Pins;
            var spi = Device.CreateSpiBus(0, 5_000_000.Hertz());
            Resolver.Log.Info("SPI bus created for CAN controller.");
            Resolver.Log.Info("Initializing MCP2515 CAN controller...");
            var mcp = new Mcp2515(spi, pins.Pin24, Mcp2515.CanOscillator.Osc_16MHz, pins.GPIO25);
            Resolver.Log.Info("MCP2515 initialized. Creating bus...");
            var bus = mcp.CreateCanBus(CanBitrate.Can_500kbps);
            Resolver.Log.Info("CAN bus created successfully.");
            _busReady.TrySetResult((bus, "CAN bus connected (MCP2515, 500 kbps)"));
            Resolver.Services.Add<ICanBus>(bus);
        }
        catch (Exception ex)
        {
            Resolver.Log.Error($"Failed to initialize CAN bus: {ex}");

            _busReady.TrySetResult((new NullCanBus(), $"Offline mode — {ex.Message}"));
        }

        return base.MeadowInitialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(new MainWindowViewModel("Connecting to hardware..."));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
