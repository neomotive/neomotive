using System.Text.Json.Serialization;

namespace Neomotive.Update;

public sealed class UpdateState
{
    [JsonPropertyName("slot")]
    public string Slot { get; set; } = "current";  // "current" | "previous"

    [JsonPropertyName("pendingVersion")]
    public string? PendingVersion { get; set; }
}
