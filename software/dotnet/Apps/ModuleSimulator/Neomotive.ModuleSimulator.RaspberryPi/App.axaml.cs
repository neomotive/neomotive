using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Meadow;
using Meadow.Avalonia;
using Meadow.Hardware;
using Neomotive.ModuleSimulator.UI;
using System;
using System.Threading.Tasks;

namespace Neomotive.ModuleSimulator;

public partial class App : AvaloniaMeadowApplication<Meadow.RaspberryPi>
{
    private readonly TaskCompletionSource<(ICanBus bus, string feedback)> _busReady = new();
    private readonly TaskCompletionSource<SimulatorInputBoard?> _inputsReady = new();
    private WaveshareDualCanHat _hat;
    private MainWindowViewModel? _mainVm;

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

        try
        {
            Resolver.Log.Info("Initializing input board...");
            _inputsReady.TrySetResult(new SimulatorInputBoard(Device!));
        }
        catch (Exception ex)
        {
            Resolver.Log.Error($"Failed to initialize input board: {ex}");
            _inputsReady.TrySetResult(null);
        }

        return base.MeadowInitialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Resolver.Log.LogLevel = Meadow.Logging.LogLevel.Trace;

            _mainVm = new MainWindowViewModel(null, "Connecting to hardware...");
            desktop.MainWindow = new MainWindow(_mainVm);
            _ = WireInputsAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task WireInputsAsync()
    {
        var inputs = await _inputsReady.Task;
        if (inputs != null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _mainVm?.SetInputs(inputs));
    }
}
