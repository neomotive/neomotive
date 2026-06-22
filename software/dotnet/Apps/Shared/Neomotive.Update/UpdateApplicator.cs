using System.Text.Json;

namespace Neomotive.Update;

/// <summary>
/// Manages the A/B slot swap for both Windows and Linux.
///
/// Layout relative to baseDir:
///   app-current/   ← active binary
///   app-previous/  ← last known-good
///   app-staging/   ← extracted during update (cleaned on success or failure)
///   config/        ← external catalog/config JSON files
///   update-state.json
/// </summary>
public sealed class UpdateApplicator(string baseDir)
{
    public string CurrentDir  => Path.Combine(baseDir, "app-current");
    public string PreviousDir => Path.Combine(baseDir, "app-previous");
    public string StagingDir  => Path.Combine(baseDir, "app-staging");
    public string ConfigDir   => Path.Combine(baseDir, "config");
    private string StateFile  => Path.Combine(baseDir, "update-state.json");

    public UpdateState ReadState()
    {
        if (!File.Exists(StateFile))
            return new UpdateState();

        try
        {
            var json = File.ReadAllText(StateFile);
            return JsonSerializer.Deserialize<UpdateState>(json) ?? new UpdateState();
        }
        catch
        {
            return new UpdateState();
        }
    }

    private void WriteState(UpdateState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StateFile, json);
    }

    /// <summary>
    /// Applies a staged update (staging dir must already be extracted + verified).
    /// For app updates: renames current→previous, staging→current, writes pending state.
    /// For config-only updates: moves config/ files into place immediately.
    /// Returns true if a restart is required (Windows always; Pi never — caller restarts itself).
    /// </summary>
    public bool Apply(UpdateManifest manifest)
    {
        if (manifest.Type == "config-only")
        {
            ApplyConfigOnly();
            return false;
        }

        ApplyAppUpdate(manifest);
        return OperatingSystem.IsWindows();
    }

    private void ApplyAppUpdate(UpdateManifest manifest)
    {
        // Move staging/app/ content to become the new app-current
        var stagingApp = Path.Combine(StagingDir, "app");

        // Also move config overrides if present
        var stagingConfig = Path.Combine(StagingDir, "config");

        // Retire current → previous (delete old previous first)
        if (Directory.Exists(PreviousDir))
            Directory.Delete(PreviousDir, recursive: true);

        if (Directory.Exists(CurrentDir))
            Directory.Move(CurrentDir, PreviousDir);

        // Promote staging/app → current
        var newCurrentSource = Directory.Exists(stagingApp) ? stagingApp : StagingDir;
        Directory.Move(newCurrentSource, CurrentDir);

        // Apply config overrides from staging if present
        if (Directory.Exists(stagingConfig))
            CopyDirectory(stagingConfig, ConfigDir);

        // Clean up staging remainder
        if (Directory.Exists(StagingDir))
            Directory.Delete(StagingDir, recursive: true);

        WriteState(new UpdateState { PendingVersion = manifest.Version });
    }

    private void ApplyConfigOnly()
    {
        var stagingConfig = Path.Combine(StagingDir, "config");
        if (!Directory.Exists(stagingConfig))
            stagingConfig = StagingDir;

        CopyDirectory(stagingConfig, ConfigDir);

        if (Directory.Exists(StagingDir))
            Directory.Delete(StagingDir, recursive: true);
    }

    /// <summary>
    /// Called at app startup to clear a pending-version marker written by the previous instance.
    /// </summary>
    public void ClearPendingState()
    {
        var state = ReadState();
        if (state.PendingVersion != null)
        {
            state.PendingVersion = null;
            WriteState(state);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
