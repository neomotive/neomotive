using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Meadow;
using Meadow.Avalonia;
using Meadow.Foundation.ICs.CAN;
using Meadow.Hardware;
using Neomotive.ScanTool.Core;
using System;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.UI;

public partial class App : AvaloniaMeadowApplication<Meadow.Windows>
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = LoadMeadowOS();
    }

    public override Task MeadowInitialize()
    {
        ICanBus bus;
        try
        {
            bus = new PCanUsb().CreateCanBus(CanBitrate.Can_500kbps);
        }
        catch
        {
            bus = new NullCanBus();
        }

        Resolver.Services.Add<ICanBus>(bus);

        return base.MeadowInitialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var bus     = Resolver.Services.Get<ICanBus>() ?? new NullCanBus();
            var scanner = new Obd2Scanner(bus);
            var vm      = new MainWindowViewModel(scanner);

            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
