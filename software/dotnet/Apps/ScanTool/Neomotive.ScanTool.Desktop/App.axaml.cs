using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Meadow;
using Meadow.Avalonia;
using Meadow.Foundation.ICs.CAN;
using Meadow.Hardware;
using Meadow.Logging;
using Neomotive.ScanTool.Core;
using System;
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

        Resolver.Services.Add<ICanBus>(bus);

        var scanner = new Obd2Scanner(bus);
        var vm      = new MainWindowViewModel(scanner);

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow != null)
                _mainWindow.DataContext = vm;
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
