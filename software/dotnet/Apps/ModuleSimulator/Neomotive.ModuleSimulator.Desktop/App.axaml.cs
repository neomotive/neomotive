using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Meadow;
using Meadow.Avalonia;
using Meadow.Foundation.ICs.CAN;
using Meadow.Hardware;
using Neomotive.ModuleSimulator.UI.Toolbox;
using Neomotive.Update;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Neomotive.ModuleSimulator.UI;

public partial class App : AvaloniaMeadowApplication<Meadow.Windows>
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = LoadMeadowOS();
    }

    private UpdateService? _updateService;

    public override Task MeadowInitialize()
    {
        Resolver.Log.AddProvider(new Meadow.Logging.DebugLogProvider());
        Resolver.Log.LogLevel = Meadow.Logging.LogLevel.Trace;

        var appDir = AppContext.BaseDirectory;
        var baseDir = Path.GetFileName(appDir.TrimEnd(Path.DirectorySeparatorChar))
                          .Equals("app-current", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(appDir)!
            : appDir;

        var appConfig = LoadAppConfig(baseDir);

        var currentVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        _updateService = new UpdateService("simulator", currentVersion, baseDir);
        _updateService.AcknowledgeStartup();
        _updateService.Configure(appConfig.UpdateServerUrl);
        _updateService.StartUsbWatcher();

        ICanBus rawBus;
        try
        {
            Resolver.Log.Info("Simulator: initializing PCAN USB adapter at 500 kbps...");
            rawBus = new PCanUsb().CreateCanBus(CanBitrate.Can_500kbps);
            Resolver.Log.Info($"Simulator: PCAN USB adapter initialized ({rawBus.GetType().Name}).");
        }
        catch (Exception ex)
        {
            Resolver.Log.Warn($"Simulator: PCAN USB init failed ({ex.GetType().Name}: {ex.Message}) — using NullCanBus.");
            rawBus = new NullCanBus();
        }

        Resolver.Services.Add<ICanBus>(rawBus);

        return base.MeadowInitialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var inputs = new DesktopInputs();

            var mainVm = new MainWindowViewModel(inputs, updateService: _updateService);
            var toolboxVm = new ToolboxViewModel(inputs, mainVm.InputsVm);
            Resolver.Services.Add<ToolboxViewModel>(toolboxVm);

            desktop.MainWindow = new DesktopShellWindow(mainVm, toolboxVm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static AppConfig LoadAppConfig(string baseDir)
    {
        var path = Path.Combine(baseDir, "neomotive.config.json");
        if (!File.Exists(path)) return new AppConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch { return new AppConfig(); }
    }
}
