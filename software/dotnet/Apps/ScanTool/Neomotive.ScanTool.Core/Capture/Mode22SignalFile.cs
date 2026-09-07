using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Loads and saves the user-defined Mode $22 channel list, so vehicle-specific identifiers can be
/// added without a rebuild.
/// </summary>
public static class Mode22SignalFile
{
    public const string DefaultFileName = "mode22-signals.json";

    /// <summary>
    /// Reads the definition file, returning an empty list when it is absent or unreadable. A bad
    /// config file must not stop the tool from doing standard OBD work.
    /// </summary>
    public static IReadOnlyList<Mode22SignalDefinition> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<Mode22SignalDefinition>();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<Mode22SignalDefinition>>(
                File.ReadAllText(path), CaptureFile.JsonOptions);

            return loaded ?? (IReadOnlyList<Mode22SignalDefinition>)Array.Empty<Mode22SignalDefinition>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<Mode22SignalDefinition>();
        }
    }

    public static void Save(string path, IReadOnlyList<Mode22SignalDefinition> definitions)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(definitions, CaptureFile.JsonOptions));
    }

    /// <summary>
    /// Writes a starter file if none exists, so the format is discoverable on the device.
    /// </summary>
    /// <remarks>
    /// The entries are a <em>template</em>. Mode $22 identifiers are manufacturer-specific and are
    /// not published; the DIDs and scaling below are placeholders that must be confirmed against
    /// the actual ECU — typically by logging known-good data and comparing, or from a service
    /// database — before any reading is trusted. They are marked disabled for that reason.
    /// </remarks>
    public static bool WriteTemplateIfMissing(string path)
    {
        if (File.Exists(path))
        {
            return false;
        }

        Save(path,
        [
            new Mode22SignalDefinition
            {
                Key = "CommandedRailPressure_UNVERIFIED",
                Name = "Commanded Rail Pressure (UNVERIFIED)",
                Unit = "kPa",
                Did = 0x0000,
                ByteOffset = 0,
                ByteLength = 2,
                Scale = 10,
                Min = 0,
                Max = 250_000,
            },
            new Mode22SignalDefinition
            {
                Key = "FcaDutyCycle_UNVERIFIED",
                Name = "Fuel Control Actuator Duty (UNVERIFIED)",
                Unit = "%",
                Did = 0x0000,
                ByteOffset = 0,
                ByteLength = 1,
                Scale = 100.0 / 255,
                Min = 0,
                Max = 100,
            },
        ]);

        return true;
    }
}
