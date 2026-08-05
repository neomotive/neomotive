using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Meadow;
using Meadow.Avalonia;
using Meadow.Hardware;
using Meadow.Logging;
using Neomotive.ScanTool.Core;
using Neomotive.ScanTool.UI.Views;
using Neomotive.Vin.Contracts;
using Neomotive.Vin.Core;
using Neomotive.Vin.Data;
using Neomotive.Vin.Extensions;
using Neomotive.Vin.Http;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.UI;

public partial class App : AvaloniaMeadowApplication<Meadow.RaspberryPi>
{
    // DRM/KMS uses a single-view lifetime, so the root is ScanToolView (the same
    // 800x480 control MainWindow hosts on the desktop) rather than a Window.
    // The view is created in OnFrameworkInitializationCompleted; DataContext is
    // assigned later from MeadowInitialize once the CAN bus is known.
    private ScanToolView? _rootView;
    private WaveshareDualCanHat? _hat;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = LoadMeadowOS();
    }

    public override Task MeadowInitialize()
    {
        // Opt-in: UdpLogger emits "UDP ERR: Network is unreachable" after every
        // line when there is no route for its broadcast, which doubles the
        // volume of a journal that lives in RAM. journalctl over SSH is the
        // normal way to read these; set SCANTOOL_UDP_LOG=1 to also ship them.
        if (Environment.GetEnvironmentVariable("SCANTOOL_UDP_LOG") == "1")
            Resolver.Log.AddProvider(new UdpLogger());
        Resolver.Log.LogLevel = LogLevel.Trace;

        // Under the Pi Appliance Kit the app lives at /data/app, which is the
        // only writable location on the device.
        var baseDir = AppContext.BaseDirectory;
        Directory.CreateDirectory(Path.Combine(baseDir, "data"));
        Directory.CreateDirectory(Path.Combine(baseDir, "config"));

        ICanBus bus;
        string adapterHint;
        try
        {
            // SCANTOOL_CAN_CHANNEL picks the HAT channel (0 = default, 1 = second).
            // Handy for isolating a faulty transceiver: a channel that transmits
            // but reports TEC climbing with REC stuck at 0 has a dead receive
            // path, and swapping channels tells you whether that is the board.
            var channel = Environment.GetEnvironmentVariable("SCANTOOL_CAN_CHANNEL") == "1" ? 1 : 0;

            Resolver.Log.Info($"Initializing Waveshare dual MCP2515 CAN HAT (CAN{channel}) at 500 kbps...");
            _hat = new WaveshareDualCanHat(Device!);
            bus = channel == 1 ? _hat.CAN1 : _hat.CAN0;
            Resolver.Log.Info($"CAN{channel} initialized successfully ({bus.GetType().Name}).");
            adapterHint = "Plug the CAN HAT into the vehicle OBD2 port.";
        }
        catch (Exception ex)
        {
            Resolver.Log.Warn($"CAN HAT init failed ({ex.GetType().Name}: {ex.Message}) — using NullCanBus.");
            bus = new NullCanBus();
            // Surface the failure in the UI — otherwise offline mode looks
            // identical to "connected but the vehicle isn't responding".
            adapterHint = $"CAN HAT not available ({ex.GetType().Name}) — running offline.";
        }

        var log = new CanPacketLog(200);
        var loggingBus = new LoggingCanBus(bus, log);

        // Surface controller-level faults. A node that can transmit but not
        // receive never sees the ACK bit, so the MCP2515 retransmits forever and
        // its transmit error counter climbs to error-passive then bus-off. That
        // is invisible at the frame layer — TX "succeeds" and RX is simply
        // silent — so without this the only symptom is a bare timeout.
        // CanErrorInfo has no useful ToString(), so format the counters explicitly —
        // TEC is the number that matters (>=128 is error-passive, 255 is bus-off).
        loggingBus.BusError += (_, e) =>
            Resolver.Log.Warn($"CAN bus error: TEC={e.TransmitErrorCount} REC={e.ReceiveErrorCount}");

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

        // No UpdateService on the appliance: updates are delivered by rsyncing a
        // new payload into /data/app (see scripts/pi/README.md), not A/B slots.
        var vm = new MainWindowViewModel(scanner, loggingBus, vinDecoder) { AdapterHint = adapterHint };

        Dispatcher.UIThread.Post(() =>
        {
            if (_rootView != null)
                _rootView.DataContext = vm;
            vm.StartCanLogTimer();
        });

        return base.MeadowInitialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _rootView = new ScanToolView { Width = 800, Height = 480 };

        // Letterbox rather than stretch if the panel reports a different mode
        // than the 800x480 the views are authored against.
        var root = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = _rootView,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = root;
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Only hit when running this project on a dev box for layout checks.
            desktop.MainWindow = new Window
            {
                Title = "Neomotive Scan Tool",
                Width = 800,
                Height = 480,
                Background = new SolidColorBrush(Avalonia.Media.Color.Parse("#111418")),
                Content = root
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
