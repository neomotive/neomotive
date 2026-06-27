using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Neomotive.ModuleSimulator;

namespace Neomotive.ModuleSimulator.UI;

public enum UnitsOfMeasure { Imperial, Metric }

public class QuickDtcConfig
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}

public class PotInputConfig
{
    public string PidKey   { get; set; } = "";
    public string Unit     { get; set; } = "";
    public double Scale    { get; set; }
    public double Offset   { get; set; }
    public PotUnitType UnitType { get; set; }
}

public class BoolInputConfig
{
    public string ChannelKey { get; set; } = "";
}

public class InputRowConfig
{
    public PotInputConfig?  Pot  { get; set; }
    public BoolInputConfig? Bool { get; set; }
}

public class SimulatorConfig
{
    public UnitsOfMeasure Units { get; set; } = UnitsOfMeasure.Imperial;
    public string Vin { get; set; } = "1M8GDM9AXKP042788";
    public int CanLogMaxDepth { get; set; } = 100;
    public ObservableCollection<QuickDtcConfig> QuickDtcs { get; set; } = [];

    /// <summary>Keyed by row label — "POT1", "SWITCH3", etc.</summary>
    public Dictionary<string, InputRowConfig> Inputs { get; set; } = [];
}

public static class ConfigManager
{
    private static string _dataDir = AppContext.BaseDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string ConfigPath => Path.Combine(_dataDir, "neoteric.config.json");

    public static void SetDataDir(string path)
    {
        _dataDir = path;
        Directory.CreateDirectory(path);
    }

    public static SimulatorConfig Load()
    {
        try
        {
            // One-time migration: move config from old location (next to binary) into data dir
            var legacyPath = Path.Combine(AppContext.BaseDirectory, "neoteric.config.json");
            if (!File.Exists(ConfigPath) && File.Exists(legacyPath))
                File.Copy(legacyPath, ConfigPath);

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
