using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Meadow;
using Meadow.Avalonia;
using Meadow.Foundation.ICs.CAN;
using Meadow.Hardware;
using Meadow.Logging;
using Neomotive.ScanTool.Core;
using Neomotive.Vin.Contracts;
using Neomotive.Vin.Core;
using Neomotive.Vin.Data;
using Neomotive.Vin.Extensions;
using Neomotive.Vin.Http;
using System;
using System.Net.Http;
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

        ICanBus bus;
        try
        {
            Resolver.Log.Info("Initializing PCAN USB adapter at 500 kbps...");
            bus = new PCanUsb().CreateCanBus(CanBitrate.Can_500kbps);
            Resolver.Log.Info($"PCAN USB adapter initialized successfully ({bus.GetType().Name}).");
        }
        catch (Exception ex)
        {
            Resolver.Log.Warn($"PCAN USB init failed ({ex.GetType().Name}: {ex.Message}) — using NullCanBus.");
            bus = new NullCanBus();
        }

        var log = new CanPacketLog(200);
        var loggingBus = new LoggingCanBus(bus, log);
        Resolver.Services.Add<ICanBus>(loggingBus);

        var scanner = new Obd2Scanner(loggingBus);

        var vinOpts = new VinOptions();
        IVinDecoder vinDecoder = new VinDecoder(
            new VinValidator(),
            new ManufacturerProvider(),
            new NhtsaClient(new HttpClient { BaseAddress = vinOpts.NhtsaBaseAddress }),
            vinOpts);

        var vm = new MainWindowViewModel(scanner, loggingBus, vinDecoder);

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow != null)
                _mainWindow.DataContext = vm;
            vm.StartCanLogTimer();
        });

        return base.MeadowInitialize();
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
