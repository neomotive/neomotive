using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neomotive.ScanTool.UI;

/// <summary>
/// User preferences, persisted as JSON alongside the captures in the app's data directory.
/// Deliberately separate from <c>neomotive.config.json</c>: that file is deployment
/// configuration written by the installer, this one is written by the operator at runtime and
/// must survive an update, which is exactly what <c>data/</c> is for.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Off by default. The appliance panel is touch-only and has no hover to summon a tooltip
    /// with, so every control carries its meaning in a visible label; tooltips are a supplement
    /// for desktop runs, where there is a mouse to reveal them.
    /// </summary>
    [JsonPropertyName("showToolTips")]
    public bool ShowToolTips { get; set; }

    /// <summary>
    /// Target period for one live-data sweep, in milliseconds. A target, not a delay: a sweep that
    /// overruns simply runs back to back. The right value depends on how many PIDs are selected and
    /// how fast the module answers, so it is the operator's to set.
    /// </summary>
    [JsonPropertyName("livePollPeriodMs")]
    public int LivePollPeriodMs { get; set; } = 100;

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt or unreadable settings file must not stop the tool from starting; the
            // defaults are always usable, and the next save overwrites the bad file.
            return new AppSettings();
        }
    }

    public bool TrySave(string path, out string error)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
}
