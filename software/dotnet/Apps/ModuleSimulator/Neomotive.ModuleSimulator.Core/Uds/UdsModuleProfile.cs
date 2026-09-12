using Meadow.Foundation.Telematics.Uds;

namespace Neomotive.ModuleSimulator;

/// <summary>
/// The simulated UDS bus: which modules answer and what they report. Serialized into the
/// simulator config file, so a bench setup is a config edit rather than a code change.
/// </summary>
public class UdsConfig
{
    /// <summary>Turns the whole UDS server side on or off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The modules that answer UDS requests.</summary>
    public List<UdsModuleProfile> Modules { get; set; } = [];

    /// <summary>
    /// The stock bench setup: PCM and TCU mirroring the simulator's own DTCs, plus three
    /// UDS-only modules so a module discovery has more than two things to find.
    /// </summary>
    public static UdsConfig CreateDefault() => new()
    {
        Enabled = true,
        Modules =
        [
            new UdsModuleProfile
            {
                Name = "PCM (Powertrain)",
                ResponseId = 0x7E8,
                MirrorSimulatorDtcs = true,
                Dids = new()
                {
                    ["0xF187"] = "NEO-PCM-0001",
                    ["0xF189"] = "SW 1.4.2",
                    ["0xF191"] = "HW REV C",
                    ["0xF197"] = "PCM (Powertrain)"
                }
            },
            new UdsModuleProfile
            {
                Name = "TCU (Transmission)",
                ResponseId = 0x7E9,
                MirrorSimulatorDtcs = true,
                Dids = new()
                {
                    ["0xF187"] = "NEO-TCU-0001",
                    ["0xF189"] = "SW 2.0.1",
                    ["0xF191"] = "HW REV B",
                    ["0xF197"] = "TCU (Transmission)"
                }
            },
            new UdsModuleProfile
            {
                Name = "BCM (Body)",
                ResponseId = 0x7EA,
                Dids = new()
                {
                    ["0xF187"] = "NEO-BCM-0001",
                    ["0xF189"] = "SW 0.9.7",
                    ["0xF197"] = "BCM (Body)"
                },
                Dtcs = [new UdsDtcProfile { Code = "B1318-17", Status = "Confirmed,TestFailed" }]
            },
            new UdsModuleProfile
            {
                Name = "ABS (Brakes)",
                ResponseId = 0x7EC,
                Dids = new()
                {
                    ["0xF187"] = "NEO-ABS-0001",
                    ["0xF189"] = "SW 3.1.0",
                    ["0xF197"] = "ABS (Brakes)"
                },
                Dtcs = [new UdsDtcProfile { Code = "C0035-11", Status = "Confirmed" }]
            },
            new UdsModuleProfile
            {
                Name = "SRS (Airbag)",
                ResponseId = 0x7ED,
                Dids = new()
                {
                    ["0xF187"] = "NEO-SRS-0001",
                    ["0xF189"] = "SW 1.0.0",
                    ["0xF197"] = "SRS (Airbag)"
                },
                Dtcs = [new UdsDtcProfile { Code = "B0051-13", Status = "Pending" }]
            },

            // The two below sit outside the legislated 0x7E0-0x7E7 window on purpose. A simulator
            // whose every module answers there cannot tell a working three-tier sweep from the
            // old legislated-only one — which is exactly how a real vehicle showing one module
            // looked like correct behaviour on the bench.
            new UdsModuleProfile
            {
                Name = "PSCM (Steering)",
                ResponseId = 0x768,   // 11-bit manufacturer range; requests arrive at 0x760
                Dids = new()
                {
                    ["0xF187"] = "NEO-PSCM-0001",
                    ["0xF189"] = "SW 2.4.1",
                    ["0xF197"] = "PSCM (Steering)"
                },
                Dtcs = [new UdsDtcProfile { Code = "C1B00-49", Status = "Confirmed" }]
            },
            new UdsModuleProfile
            {
                Name = "IPC (Cluster, 29-bit)",
                Extended = true,
                EcuAddress = 0x20,    // requests 0x18DA20F1, responses 0x18DAF120
                Dids = new()
                {
                    ["0xF187"] = "NEO-IPC-0001",
                    ["0xF189"] = "SW 5.0.2",
                    ["0xF197"] = "IPC (Cluster, 29-bit)"
                },
                Dtcs = [new UdsDtcProfile { Code = "U0155-87", Status = "Confirmed" }]
            }
        ]
    };
}

/// <summary>One simulated ECU on the UDS bus.</summary>
public class UdsModuleProfile
{
    /// <summary>Display name. Also the fallback for DID $F197 when none is configured.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The address this module answers from, e.g. 0x7E8. For an 11-bit module requests arrive at
    /// that minus 8; for an extended one see <see cref="EcuAddress"/>, which supersedes this.
    /// </summary>
    public ushort ResponseId { get; set; }

    /// <summary>
    /// When true the module is served on a 29-bit normal-fixed address instead of an 11-bit one:
    /// requests at <c>0x18DA{EcuAddress}F1</c>, responses at <c>0x18DAF1{EcuAddress}</c>.
    /// <para>
    /// This is what lets the extended discovery tier be exercised on the bench. Several
    /// manufacturers put everything outside the powertrain on 29-bit addressing, so a simulator
    /// that can only speak 11-bit cannot prove the sweep works.
    /// </para>
    /// </summary>
    public bool Extended { get; set; }

    /// <summary>
    /// The ECU address byte for an extended module. Ignored unless <see cref="Extended"/> is set.
    /// </summary>
    public byte EcuAddress { get; set; }

    /// <summary>Whether this module is served at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// DID values, keyed like "0xF190". A value may carry an encoding prefix — <c>ascii:TEXT</c>
    /// or <c>hex:01 02 03</c>. Without one, the catalog's encoding for that DID decides, falling
    /// back to ASCII.
    /// </summary>
    public Dictionary<string, string> Dids { get; set; } = [];

    /// <summary>Static DTCs this module always reports.</summary>
    public List<UdsDtcProfile> Dtcs { get; set; } = [];

    /// <summary>
    /// When true, the module also reports the simulator's own DTC stores, and a UDS clear
    /// clears them — so the toolbox fault buttons drive OBD-II and UDS alike.
    /// </summary>
    public bool MirrorSimulatorDtcs { get; set; }
}

/// <summary>A statically configured DTC.</summary>
public class UdsDtcProfile
{
    /// <summary>The code, optionally with a fault type byte: "P0300" or "P0300-13".</summary>
    public string Code { get; set; } = "";

    /// <summary>
    /// Status bits, either named ("Confirmed,TestFailed") or numeric ("0x09").
    /// Defaults to confirmed + test failed.
    /// </summary>
    public string Status { get; set; } = "Confirmed,TestFailed";

    /// <summary>Parses <see cref="Status"/> into its mask.</summary>
    public UdsDtcStatusMask ParseStatus()
    {
        var text = Status?.Trim();
        if (string.IsNullOrEmpty(text)) return UdsDtcStatusMask.ConfirmedDtc | UdsDtcStatusMask.TestFailed;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            byte.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out var raw))
        {
            return (UdsDtcStatusMask)raw;
        }

        UdsDtcStatusMask mask = 0;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Accept the short names used in the UI ("Confirmed", "Pending") as well as the
            // full enum member names.
            var name = part switch
            {
                "Confirmed" => nameof(UdsDtcStatusMask.ConfirmedDtc),
                "Pending" => nameof(UdsDtcStatusMask.PendingDtc),
                "Active" => nameof(UdsDtcStatusMask.TestFailed),
                "MIL" => nameof(UdsDtcStatusMask.WarningIndicatorRequested),
                _ => part
            };

            if (Enum.TryParse<UdsDtcStatusMask>(name, ignoreCase: true, out var bit)) mask |= bit;
        }

        return mask == 0 ? UdsDtcStatusMask.ConfirmedDtc | UdsDtcStatusMask.TestFailed : mask;
    }
}
