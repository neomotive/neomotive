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

    public override Task MeadowRun()
    {
        return base.MeadowRun();
    }

    public override Task MeadowInitialize()
    {
        ICanBus rawBus;
        string feedback;
        try
        {
            rawBus = new PCanUsb().CreateCanBus(CanBitrate.Can_500kbps);
            feedback = "CAN bus connected (PCanUsb, 500 kbps)";
        }
        catch (Exception ex)
        {
            rawBus = new NullCanBus();
            feedback = $"Offline mode — {ex.Message}";
        }

        Resolver.Services.Add<ICanBus>(rawBus);

        return base.MeadowInitialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var toolboxVm = new ToolboxViewModel();
            Resolver.Services.Add<ToolboxViewModel>(toolboxVm);

            desktop.MainWindow = new DesktopShellWindow(new MainWindowViewModel(), toolboxVm);
        }

        base.OnFrameworkInitializationCompleted();
    }
}