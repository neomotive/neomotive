using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Neomotive.ScanTool.Core.Capture;

namespace Neomotive.ScanTool.Core.Diagnostics;

/// <summary>
/// Loads and saves the diagnostic profile library from the config directory.
/// </summary>
public static class DiagnosticProfileFile
{
    public const string DefaultFileName = "diagnostic-profiles.json";

    /// <summary>
    /// Reads the profile file. A missing or unreadable file falls back to the built-in library so
    /// the tool is always usable — a bad edit must not leave the operator with no profiles.
    /// </summary>
    public static IReadOnlyList<DiagnosticProfile> Load(string path)
    {
        if (!File.Exists(path))
        {
            return DiagnosticProfileLibrary.BuiltIn;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<DiagnosticProfile>>(
                File.ReadAllText(path), CaptureFile.JsonOptions);

            if (loaded is null || loaded.Count == 0)
            {
                return DiagnosticProfileLibrary.BuiltIn;
            }

            // Ignore entries with no key: they cannot be selected or referenced.
            return loaded.Where(p => !string.IsNullOrWhiteSpace(p.Key)).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return DiagnosticProfileLibrary.BuiltIn;
        }
    }

    public static void Save(string path, IReadOnlyList<DiagnosticProfile> profiles)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(profiles, CaptureFile.JsonOptions));
    }

    /// <summary>
    /// Writes the built-in library to disk if no file exists yet, so the format is discoverable
    /// and the shipped profiles can be edited rather than only replaced.
    /// </summary>
    public static bool WriteDefaultsIfMissing(string path)
    {
        if (File.Exists(path))
        {
            return false;
        }

        Save(path, DiagnosticProfileLibrary.BuiltIn);
        return true;
    }

    /// <summary>
    /// Adds any built-in profile whose key is absent from the file, preserving user edits and
    /// user-authored profiles. Returns the merged list; only writes when something was added.
    /// </summary>
    public static IReadOnlyList<DiagnosticProfile> MergeNewDefaults(
        string path,
        IReadOnlyList<DiagnosticProfile> current)
    {
        var keys = current.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = DiagnosticProfileLibrary.BuiltIn.Where(p => !keys.Contains(p.Key)).ToArray();

        if (added.Length == 0)
        {
            return current;
        }

        var merged = current.Concat(added).ToArray();

        try
        {
            Save(path, merged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only config; the merged list is still returned for this session.
        }

        return merged;
    }
}
