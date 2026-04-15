using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Meadow;
using Meadow.Avalonia;
using Meadow.Hardware;
using Neomotive.ModuleSimulator.UI;
using System;
using System.Threading.Tasks;

namespace Neomotive.ModuleSimulator.RaspberryPi;

public partial class App : AvaloniaMeadowApplication<Meadow.RaspberryPi>
{
    private readonly TaskCompletionSource<(ICanBus bus, string feedback)> _busReady = new();
    private WaveshareDualCanHat _hat;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = LoadMeadowOS();
    }

    public override Task MeadowInitialize()
    {
        Console.WriteLine("Initializing Meadow application...");

        Resolver.Log.AddProvider(new Meadow.Logging.UdpLogger());

        try
        {
            Resolver.Log.Info("Initializing CAN bus...");

            _hat = new WaveshareDualCanHat(Device!);
            Resolver.Services.Add<ICanBus>(_hat.CAN0);
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
