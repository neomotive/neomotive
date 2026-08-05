using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Meadow;
using Meadow.Avalonia;
using Meadow.Foundation.ICs.CAN;
using Meadow.Hardware;
using Meadow.Logging;
using Neomotive.ScanTool.Core;
using Neomotive.Update;
using Neomotive.Vin.Contracts;
using Neomotive.Vin.Core;
using Neomotive.Vin.Data;
using Neomotive.Vin.Extensions;
using Neomotive.Vin.Http;
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.UI;

public partial class App : AvaloniaMeadowApplication<Meadow.Windows>
{
    // Window is created in OnFrameworkInitializationCompleted (Avalonia requirement).
    // DataContext is set later from MeadowInitialize once the real bus is known,
    // avoiding the race where OnFrameworkInitializationCompleted runs before MeadowInitialize.
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = LoadMeadowOS();
    }

    public override Task MeadowInitialize()
    {
        Resolver.Log.AddProvider(new DebugLogProvider());
        Resolver.Log.LogLevel = LogLevel.Trace;

        // Base dir is the parent of app-current/ (or the publish dir itself for dev runs)
        var appDir = AppContext.BaseDirectory;
        var baseDir = Path.GetFileName(appDir.TrimEnd(Path.DirectorySeparatorChar))
                          .Equals("app-current", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(appDir)!
            : appDir;

        var appConfig = LoadAppConfig(baseDir);
        Directory.CreateDirectory(Path.Combine(baseDir, "data"));

        ICanBus bus;
        string adapterHint;
        try
        {
            Resolver.Log.Info("Initializing PCAN USB adapter at 500 kbps...");
            bus = new PCanUsb().CreateCanBus(CanBitrate.Can_500kbps);
            Resolver.Log.Info($"PCAN USB adapter initialized successfully ({bus.GetType().Name}).");
            adapterHint = "Connect a Peak PCAN USB adapter and plug into vehicle OBD2 port.";
        }
        catch (Exception ex)
        {
            Resolver.Log.Warn($"PCAN USB init failed ({ex.GetType().Name}: {ex.Message}) — using NullCanBus.");
            bus = new NullCanBus();
            adapterHint = $"PCAN USB adapter not available ({ex.GetType().Name}) — running offline.";
        }

        var log = new CanPacketLog(200);
        var loggingBus = new LoggingCanBus(bus, log);
        Resolver.Services.Add<ICanBus>(loggingBus);

        var scanner = new Obd2Scanner(loggingBus);

        var vinOpts = new VinOptions
        {
            ExternalCatalogPath = Path.Combine(baseDir, "config")
        };
        IVinDecoder vinDecoder = new VinDecoder(
            new VinValidator(),
            new ManufacturerProvider(vinOpts),
            new NhtsaClient(new HttpClient { BaseAddress = vinOpts.NhtsaBaseAddress }),
            vinOpts);

        var currentVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        var updateService = new UpdateService("scantool", currentVersion, baseDir);
        updateService.AcknowledgeStartup();
        updateService.Configure(appConfig.UpdateServerUrl);
        updateService.StartUsbWatcher();

        var vm = new MainWindowViewModel(scanner, loggingBus, vinDecoder, updateService) { AdapterHint = adapterHint };

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow != null)
                _mainWindow.DataContext = vm;
            vm.StartCanLogTimer();
        });

        return base.MeadowInitialize();
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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
