namespace Neomotive.Update;

public abstract class UpdateResult
{
    private UpdateResult() { }

    public sealed class NotAvailable : UpdateResult { }

    public sealed class Available(UpdateManifest manifest) : UpdateResult
    {
        public UpdateManifest Manifest { get; } = manifest;
    }

    public sealed class Applied(UpdateManifest manifest) : UpdateResult
    {
        public UpdateManifest Manifest { get; } = manifest;
        public bool RequiresRestart { get; init; }
    }

    public sealed class Failed(string reason) : UpdateResult
    {
        public string Reason { get; } = reason;
    }
}
