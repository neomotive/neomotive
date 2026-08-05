namespace Neomotive.Can.UI;

/// <summary>
/// A single formatted row in the CAN packet log.
/// </summary>
public record CanLogItem(string Time, string Id, bool IsOutgoing, string Data, string Description);
