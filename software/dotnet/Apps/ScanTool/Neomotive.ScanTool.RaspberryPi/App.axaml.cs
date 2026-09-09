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
using Neomotive.Can.Hardware;
using Neomotive.ScanTool.Core;
using Neomotive.ScanTool.UI.Views;
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

public partial class App : AvaloniaMeadowApplication<Meadow.RaspberryPi>
{
    // DRM/KMS uses a single-view lifetime, so the root is ScanToolView (the same
    // 800x480 control MainWindow hosts on the desktop) rather than a Window.
    // The view is created in OnFrameworkInitializationCompleted; DataContext is
    // assigned later from MeadowInitialize once the CAN bus is known.
    private ScanToolView? _rootView;
    private WaveshareDualCanHat? _hat;
    private UpdateService? _updateService;

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

        // Under the Pi Appliance Kit the payload lives under /data/app, which is
        // the only writable location on the device. The binary itself runs from
        // the A/B slot /data/app/app-current, so baseDir — the root that holds
        // app-current/, app-previous/, config/, data/ and update-state.json — is
        // one level up. Older payloads ran straight out of /data/app; fall back
        // to that so a device that has not been migrated still starts.
        var appDir = AppContext.BaseDirectory;
        var baseDir = Path.GetFileName(appDir.TrimEnd(Path.DirectorySeparatorChar))
                          .Equals("app-current", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(appDir.TrimEnd(Path.DirectorySeparatorChar))!
            : appDir;

        Directory.CreateDirectory(Path.Combine(baseDir, "data"));
        Directory.CreateDirectory(Path.Combine(baseDir, "config"));

        ICanBus bus;
        string adapterHint;
        string canChannelName;
        try
        {
            // SCANTOOL_CAN_CHANNEL picks the HAT channel (0 = default, 1 = second).
            // Handy for isolating a faulty transceiver: a channel that transmits
            // but reports TEC climbing with REC stuck at 0 has a dead receive
            // path, and swapping channels tells you whether that is the board.
            var channel = Environment.GetEnvironmentVariable("SCANTOOL_CAN_CHANNEL") == "1" ? 1 : 0;

            canChannelName = WaveshareDualCanHat.DescribeChannel(channel);

            Resolver.Log.Info($"Initializing Waveshare dual MCP2515 CAN HAT ({canChannelName}) at 500 kbps...");
            _hat = new WaveshareDualCanHat(Device!);
            bus = _hat.GetChannel(channel);
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
            canChannelName = "offline (no CAN hardware)";
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

        // Updates: A/B slots under baseDir, same model as the simulator. The USB
        // watcher polls for a neomotive-update*.zip on removable media; the
        // network source defaults to the GitHub release manifest, and neomotive.config.json
        // only needs to exist to point this device somewhere else.
        var currentVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        _updateService = new UpdateService("scantool", currentVersion, baseDir);
        _updateService.AcknowledgeStartup();
        _updateService.Configure(LoadUpdateServerUrl(baseDir));
        _updateService.StartUsbWatcher();

        var vm = new MainWindowViewModel(scanner, loggingBus, vinDecoder, _updateService)
        {
            // baseDir is /data/app on the appliance, so this lands captures inside the only
            // writable location on the device.
            DataDirectory = Path.Combine(baseDir, "data"),
            ConfigDirectory = Path.Combine(baseDir, "config"),
            AdapterHint = adapterHint,
            CanChannelName = canChannelName
        };

        Dispatcher.UIThread.Post(() =>
        {
            if (_rootView != null)
                _rootView.DataContext = vm;
            vm.StartCanLogTimer();
        });

        return base.MeadowInitialize();
    }

    // Device-local, and deliberately outside app-current/ so an update never
    // overwrites the server this device was pointed at. Null (the shipped
    // default) means "use UpdateService.DefaultManifestUrl" — the GitHub release
    // manifest — so a stock device checks the internet with no config at all.
    private static string? LoadUpdateServerUrl(string baseDir)
    {
        var path = Path.Combine(baseDir, "neomotive.config.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("updateServerUrl", out var el)
                   && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            Resolver.Log.Warn($"Ignoring malformed neomotive.config.json: {ex.Message}");
            return null;
        }
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
