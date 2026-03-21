using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Neomotive.ModuleSimulator.UI;

public class QuickDtcConfig
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}

public class SimulatorConfig
{
    public ObservableCollection<QuickDtcConfig> QuickDtcs { get; set; } = [];
}

public static class ConfigManager
{
    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "neoteric.config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SimulatorConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<SimulatorConfig>(json, JsonOptions)
                    ?? new SimulatorConfig();
            }
        }
        catch { }
        return new SimulatorConfig();
    }

    public static void Save(SimulatorConfig config)
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch { }
    }
}
