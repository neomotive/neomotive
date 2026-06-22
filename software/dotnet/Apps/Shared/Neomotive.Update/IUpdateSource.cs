namespace Neomotive.Update;

public interface IUpdateSource
{
    /// <summary>
    /// Checks for an available update for the given app and current version.
    /// Returns a manifest if a newer version is available, null otherwise.
    /// </summary>
    Task<(UpdateManifest Manifest, string ZipPath)?> CheckAsync(
        string appId,
        string currentVersion,
        CancellationToken ct = default);
}
