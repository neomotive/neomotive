namespace Neomotive.Uds;

/// <summary>
/// The on-disk shape of a catalog file — the seed resource and every overlay use it.
/// </summary>
public class UdsCatalogFile
{
    /// <summary>DID definitions. Later files override earlier ones by identifier.</summary>
    public List<UdsDidDefinition> Dids { get; set; } = [];

    /// <summary>Fault type byte descriptions, keyed like "0x13".</summary>
    public Dictionary<string, string> FaultTypes { get; set; } = [];

    /// <summary>Negative response code descriptions, keyed like "0x31".</summary>
    public Dictionary<string, string> Nrcs { get; set; } = [];
}

/// <summary>What an <see cref="UdsCatalog.Export"/> should write.</summary>
public enum UdsCatalogScope
{
    /// <summary>Everything the catalog currently resolves — seed plus overlays plus runtime edits.</summary>
    All,
    /// <summary>Only what was added or changed on top of the seed.</summary>
    Overrides
}
