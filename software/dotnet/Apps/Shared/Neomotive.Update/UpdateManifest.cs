using System.Text.Json.Serialization;

namespace Neomotive.Update;

public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("target")]
    public string Target { get; init; } = "";  // "scantool" | "simulator"

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = "";  // "windows" | "linux-arm64" | "any"

    [JsonPropertyName("type")]
    public string Type { get; init; } = "full";  // "full" | "config-only"

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = "";

    [JsonPropertyName("files")]
    public List<UpdateFileEntry> Files { get; init; } = [];
}

public sealed class UpdateFileEntry
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";
}
