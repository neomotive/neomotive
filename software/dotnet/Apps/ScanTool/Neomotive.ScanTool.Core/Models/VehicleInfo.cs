using System.Collections.Generic;

namespace Neomotive.ScanTool.Core;

public class VehicleInfo
{
    public string? Vin { get; set; }
    public string Protocol { get; set; } = "";
    public List<int> EcuAddresses { get; set; } = [];
}
