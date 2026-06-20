using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Meadow;
using Meadow.Avalonia;
using Meadow.Foundation.ICs.CAN;
using Meadow.Hardware;
using Neomotive.ModuleSimulator.UI.Toolbox;
using System;
using System.Threading.Tasks;

namespace Neomotive.ModuleSimulator.UI;

public partial class App : AvaloniaMeadowApplication<Meadow.Windows>
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _ = LoadMeadowOS();
    }

    public override Task MeadowInitialize()
    {
        Resolver.Log.AddProvider(new Meadow.Logging.DebugLogProvider());
        Resolver.Log.LogLevel = Meadow.Logging.LogLevel.Trace;

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

            var mainVm = new MainWindowViewModel(inputs);
            var toolboxVm = new ToolboxViewModel(inputs, mainVm.InputsVm);
            Resolver.Services.Add<ToolboxViewModel>(toolboxVm);

            desktop.MainWindow = new DesktopShellWindow(mainVm, toolboxVm);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
