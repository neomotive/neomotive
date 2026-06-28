using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Meadow;
using Meadow.Avalonia;
using Meadow.Hardware;
using Neomotive.ModuleSimulator.UI;
using Neomotive.Update;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Neomotive.ModuleSimulator;

public partial class App : AvaloniaMeadowApplication<Meadow.RaspberryPi>
{
    private readonly TaskCompletionSource<(ICanBus bus, string feedback)> _busReady = new();
    private readonly TaskCompletionSource<(SimulatorInputBoard? Board, string? Error)> _inputsReady = new();
    private WaveshareDualCanHat _hat;
    private MainWindowViewModel? _mainVm;
    private UpdateService? _updateService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = LoadMeadowOS();
    }

    public override Task MeadowInitialize()
    {
        Console.WriteLine("Initializing Meadow application...");

        var appDir = AppContext.BaseDirectory;
        var baseDir = Path.GetFileName(appDir.TrimEnd(Path.DirectorySeparatorChar))
                          .Equals("app-current", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(appDir)!
            : appDir;

        ConfigManager.SetDataDir(Path.Combine(baseDir, "data"));

        var currentVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        _updateService = new UpdateService("simulator", currentVersion, baseDir);
        _updateService.AcknowledgeStartup();
        _updateService.Configure(LoadUpdateServerUrl(baseDir));
        _updateService.StartUsbWatcher();

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
            _inputsReady.TrySetResult((new SimulatorInputBoard(Device!), null));
        }
        catch (Exception ex)
        {
            Resolver.Log.Error($"Failed to initialize input board: {ex}");
            _inputsReady.TrySetResult((null, $"{ex.GetType().Name}: {ex.Message}"));
        }

        return base.MeadowInitialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Resolver.Log.LogLevel = Meadow.Logging.LogLevel.Trace;

            _mainVm = new MainWindowViewModel(null, "Connecting to hardware...", _updateService);
            desktop.MainWindow = new MainWindow(_mainVm);
            _ = WireInputsAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task WireInputsAsync()
    {
        var (board, error) = await _inputsReady.Task;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (board != null)
                _mainVm?.SetInputs(board);
            else if (error != null)
                _mainVm?.SetInputsError(error);
        });
    }

    private static string? LoadUpdateServerUrl(string baseDir)
    {
        var path = Path.Combine(baseDir, "neomotive.config.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("updateServerUrl", out var el) ? el.GetString() : null;
        }
        catch { return null; }
    }
}
