using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Neomotive.ScanTool.Core.Capture;

namespace Neomotive.ScanTool.Core.Signals;

/// <summary>
/// The set of signals this vehicle session can read: the standard Mode $01 table plus any
/// vehicle-specific Mode $22 definitions.
/// </summary>
/// <remarks>
/// Both files deserialize to the same <see cref="SignalDefinition"/>, so a Mode $22 channel is a
/// first-class signal everywhere — live view, capture, triggers and profiles.
/// </remarks>
public sealed class SignalTable
{
    public const string PidTableFileName = "pid-table.json";
    public const string Mode22FileName = "mode22-signals.json";

    private readonly Dictionary<string, SignalDefinition> _byKey;

    public SignalTable(IEnumerable<SignalDefinition> signals)
    {
        // Later definitions win on a key clash, so a Mode $22 entry can deliberately override a
        // standard one (a manufacturer channel that reports the same quantity more usefully).
        _byKey = new Dictionary<string, SignalDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var signal in signals)
        {
            if (!string.IsNullOrWhiteSpace(signal.Key))
            {
                _byKey[signal.Key] = signal;
            }
        }

        All = _byKey.Values.OrderBy(s => s.Source).ThenBy(s => s.Address).ThenBy(s => s.ByteOffset).ToArray();
        Systems = SignalLibrary.Systems(All);
    }

    public IReadOnlyList<SignalDefinition> All { get; }

    /// <summary>System names present in this table, in display order.</summary>
    public IReadOnlyList<string> Systems { get; }

    public SignalDefinition? Find(string? key)
        => key is not null && _byKey.TryGetValue(key, out var signal) ? signal : null;

    /// <summary>
    /// Filters by free text and, optionally, a set of systems. An empty system set means "all",
    /// which is what an untouched filter row should do.
    /// </summary>
    public IReadOnlyList<SignalDefinition> Search(string? text, IReadOnlyCollection<string>? systems = null)
    {
        var query = All.Where(s => s.Matches(text));

        if (systems is { Count: > 0 })
        {
            query = query.Where(s => s.Systems.Any(
                sys => systems.Contains(sys, StringComparer.OrdinalIgnoreCase)));
        }

        return query.ToArray();
    }

    /// <summary>
    /// Loads the table from a config directory, writing the built-in Mode $01 table on first run.
    /// Falls back to the built-in table if the file is missing or unreadable.
    /// </summary>
    public static SignalTable Load(string? configDirectory)
    {
        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            return new SignalTable(SignalLibrary.BuiltIn);
        }

        var pidPath = Path.Combine(configDirectory, PidTableFileName);
        var mode22Path = Path.Combine(configDirectory, Mode22FileName);

        try
        {
            if (!File.Exists(pidPath))
            {
                Save(pidPath, SignalLibrary.BuiltIn);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only config directory; the built-in table still loads below.
        }

        var standard = ReadFile(pidPath);

        if (standard.Count == 0)
        {
            standard = SignalLibrary.BuiltIn;
        }

        return new SignalTable(standard.Concat(ReadFile(mode22Path)));
    }

    public static void Save(string path, IReadOnlyList<SignalDefinition> signals)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(signals, CaptureFile.JsonOptions));
    }

    private static IReadOnlyList<SignalDefinition> ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<SignalDefinition>();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<SignalDefinition>>(
                File.ReadAllText(path), CaptureFile.JsonOptions);

            return loaded?.Where(s => !string.IsNullOrWhiteSpace(s.Key)).ToArray()
                ?? (IReadOnlyList<SignalDefinition>)Array.Empty<SignalDefinition>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<SignalDefinition>();
        }
    }
}
