using System.Text.Json;
using Neomotive.Vin.CatalogGenerator;

// ──────────────────────────────────────────────────────────────────────────────
// Neomotive VIN Catalog Generator
// Queries the NHTSA vPIC API and writes manufacturers.json + model-catalog.json
// into Neomotive.Vin/Resources/ (or --output-dir <path>).
//
// Usage:
//   dotnet run                           # writes to ../Neomotive.Vin/Resources/
//   dotnet run -- --output-dir <path>
//   dotnet run -- --year-start 1996      # default; OBD2 became mandatory in 1996
//   dotnet run -- --makes "Ford,Toyota"  # restrict to specific makes
// ──────────────────────────────────────────────────────────────────────────────

var JsonWriteOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var opts = Args.Parse(args);

var outputDir = Path.GetFullPath(opts.OutputDir ?? "../Neomotive.Vin/Resources");
int yearStart = opts.YearStart;

Directory.CreateDirectory(outputDir);

Console.WriteLine($"Output : {outputDir}");
Console.WriteLine($"Year   : {yearStart}+");
Console.WriteLine();

// ── Curated makes for OBD2-era US market ──────────────────────────────────────
// These are the most common makes encountered by OBD2 scan tools in the US.
// Add more as needed — no code changes required after updating the JSON files.
var targetMakes = opts.Makes ?? [
    // American
    "Buick", "Cadillac", "Chevrolet", "GMC", "Oldsmobile", "Pontiac", "Saturn",
    "Ford", "Lincoln", "Mercury",
    "Chrysler", "Dodge", "Jeep", "Ram", "Plymouth",
    "Tesla",
    // Japanese
    "Acura", "Honda",
    "Infiniti", "Nissan",
    "Lexus", "Toyota",
    "Mazda",
    "Mitsubishi",
    "Subaru",
    "Suzuki",
    "Isuzu",
    "Scion",
    // Korean
    "Genesis", "Hyundai", "Kia",
    // European
    "Audi", "Volkswagen", "Porsche",
    "BMW", "Mini",
    "Mercedes-Benz",
    "Volvo",
    "Saab",
    "Jaguar", "Land Rover",
    "Alfa Romeo", "Fiat",
    "Bentley",
    "Aston Martin",
    "Maserati",
];

// Supplemental WMI map for brands whose legal manufacturer name differs from the brand name.
// NHTSA's WMI endpoint uses legal entity names (e.g. "General Motors LLC"), not brand names.
// These are well-known stable WMIs for the most common US-market brands.
var supplementalWmis = new Dictionary<string, (string Wmi, string Manufacturer, string Country, string VehicleType)>(StringComparer.OrdinalIgnoreCase)
{
    ["Buick"]      = ("1G4", "General Motors LLC", "United States", "Passenger Car"),
    ["Cadillac"]   = ("1G6", "General Motors LLC", "United States", "Passenger Car"),
    ["Chevrolet"]  = ("1G1", "General Motors LLC", "United States", "Passenger Car"),
    ["GMC"]        = ("1GT", "General Motors LLC", "United States", "Truck"),
    ["Oldsmobile"] = ("1G3", "General Motors LLC", "United States", "Passenger Car"),
    ["Pontiac"]    = ("1G2", "General Motors LLC", "United States", "Passenger Car"),
    ["Lincoln"]    = ("1LN", "Ford Motor Company", "United States", "Passenger Car"),
    ["Mercury"]    = ("1ME", "Ford Motor Company", "United States", "Passenger Car"),
    ["Dodge"]      = ("1B3", "Stellantis", "United States", "Passenger Car"),
    ["Jeep"]       = ("1J4", "Stellantis", "United States", "Multipurpose Passenger Vehicle (MPV)"),
    ["Plymouth"]   = ("1P3", "Chrysler LLC", "United States", "Passenger Car"),
    ["Fiat"]       = ("ZFA", "Fiat Group Automobiles S.p.A.", "Italy", "Passenger Car"),
    ["Acura"]      = ("JH4", "Honda Motor Co., Ltd.", "Japan", "Passenger Car"),
    ["Infiniti"]   = ("JN1", "Nissan Motor Co., Ltd.", "Japan", "Passenger Car"),
    ["Lexus"]      = ("JT8", "Toyota Motor Corporation", "Japan", "Passenger Car"),
    ["Scion"]      = ("JTK", "Toyota Motor Corporation", "Japan", "Passenger Car"),
    ["Genesis"]    = ("KMH", "Hyundai Motor Company", "South Korea", "Passenger Car"),
    ["Mini"]       = ("WMW", "BMW AG", "Germany", "Passenger Car"),
    ["Saturn"]     = ("1G8", "General Motors LLC", "United States", "Passenger Car"),
    ["Ram"]        = ("1C6", "Stellantis", "United States", "Truck"),
};

using var nhtsa = new NhtsaApiClient();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var manufacturerMap = new Dictionary<string, List<ManufacturerEntry>>(StringComparer.OrdinalIgnoreCase);
var catalogEntries = new List<CatalogEntry>();

int done = 0;
foreach (var make in targetMakes)
{
    if (cts.Token.IsCancellationRequested) break;

    Console.Write($"  [{++done,2}/{targetMakes.Length}] {make,-20} ");

    // ── WMI lookup ────────────────────────────────────────────────────────────
    List<NhtsaWmiResult> wmis = [];
    try
    {
        wmis = await nhtsa.GetWmisForManufacturerAsync(make, cts.Token);
        await Task.Delay(120, cts.Token); // gentle rate-limit
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.WriteLine($"WMI error: {ex.Message}");
    }

    // Keep EVERY car/truck WMI the make has, not one "best" one.
    //
    // This used to score the WMIs and take the winner, which is why a 2021 Ford Explorer decoded
    // to nothing: Ford has 78 WMIs, the scorer picked "1FA" (Passenger Car, USA), and "1FM" — the
    // Explorer's, and every other Ford SUV's — was thrown away along with 1FT, 1FD, 2FM and the
    // rest. A WMI is the manufacturer's identity, not a preference, so all of them belong in the
    // table; the file is a few hundred KB either way.
    static bool IsCarOrTruck(string vt) =>
        vt.Contains("Passenger", StringComparison.OrdinalIgnoreCase) ||
        vt.Contains("MPV", StringComparison.OrdinalIgnoreCase) ||
        vt.Contains("Multipurpose", StringComparison.OrdinalIgnoreCase) ||
        vt.Contains("Truck", StringComparison.OrdinalIgnoreCase);

    var makeWmis = wmis
        .Where(w => w.Wmi.Length == 3 && IsCarOrTruck(w.VehicleType))
        .GroupBy(w => w.Wmi, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .OrderBy(w => w.Wmi, StringComparer.Ordinal)
        .ToList();

    // The first one still names the make in model-catalog.json, which is keyed by make.
    var bestWmi = makeWmis.FirstOrDefault();

    // ── Model lookup ──────────────────────────────────────────────────────────
    List<NhtsaModelResult> models = [];
    try
    {
        models = await nhtsa.GetModelsForMakeAsync(make, cts.Token);
        await Task.Delay(120, cts.Token);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.WriteLine($"model error: {ex.Message}");
    }

    // Deduplicate models; NHTSA can return the same name with different IDs
    var uniqueModels = models
        .Select(m => m.ModelName.Trim())
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n)
        .ToList();

    // ── Accumulate manufacturer entries ───────────────────────────────────────
    // Every NHTSA car/truck WMI for the make, plus the supplemental one if NHTSA returned none.
    var entriesForMake = new List<(string Wmi, string Manufacturer, string Country, string VehicleType)>();

    foreach (var w in makeWmis)
    {
        entriesForMake.Add((
            w.Wmi,
            string.IsNullOrWhiteSpace(w.ManufacturerName) ? make : w.ManufacturerName,
            w.Country ?? "",
            string.IsNullOrWhiteSpace(w.VehicleType) ? "Passenger Car" : w.VehicleType));
    }

    if (entriesForMake.Count == 0 && supplementalWmis.TryGetValue(make, out var sup))
    {
        entriesForMake.Add((sup.Wmi, sup.Manufacturer, sup.Country, sup.VehicleType));
    }

    string? wmiCode = entriesForMake.Count > 0 ? entriesForMake[0].Wmi : null;

    Console.WriteLine($"WMIs={entriesForMake.Count,3}  models={uniqueModels.Count}");

    foreach (var (wmi, mfrName, mfrCountry, mfrVehicleType) in entriesForMake)
    {
        if (manufacturerMap.TryGetValue(wmi, out var existing))
        {
            // Several brands can share a WMI (Ford and Lincoln both appear under some), so the
            // makes list accumulates rather than the entry being replaced.
            if (!existing[0].Makes.Contains(make, StringComparer.OrdinalIgnoreCase))
                existing[0].Makes.Add(make);
        }
        else
        {
            manufacturerMap[wmi] = [new ManufacturerEntry
            {
                Wmi = wmi,
                Manufacturer = mfrName,
                Country = mfrCountry,
                VehicleType = mfrVehicleType,
                Makes = [make]
            }];
        }
    }

    // ── Accumulate catalog entry ───────────────────────────────────────────────
    if (uniqueModels.Count > 0)
    {
        catalogEntries.Add(new CatalogEntry
        {
            Make = make,
            Wmi = wmiCode ?? "",
            Models = uniqueModels.Select(m => new ModelEntry
            {
                Name = m,
                YearStart = yearStart,
                YearEnd = 9999,
                Packages = [],
                Plants = new Dictionary<string, string>()
            }).ToList()
        });
    }
}

// ── Write manufacturers.json ──────────────────────────────────────────────────
var manufacturers = manufacturerMap.Values
    .SelectMany(x => x)
    .OrderBy(m => m.Wmi)
    .ToList();

var manufacturersPath = Path.Combine(outputDir, "manufacturers.json");
await File.WriteAllTextAsync(manufacturersPath,
    JsonSerializer.Serialize(manufacturers, JsonWriteOptions), cts.Token);
Console.WriteLine($"\nWrote {manufacturers.Count} WMI entries → {manufacturersPath}");

// ── Write model-catalog.json ──────────────────────────────────────────────────
var catalogPath = Path.Combine(outputDir, "model-catalog.json");
await File.WriteAllTextAsync(catalogPath,
    JsonSerializer.Serialize(catalogEntries, JsonWriteOptions), cts.Token);
Console.WriteLine($"Wrote {catalogEntries.Sum(e => e.Models.Count)} models across {catalogEntries.Count} makes → {catalogPath}");

Console.WriteLine("\nDone. Rebuild Neomotive.Vin to embed updated resources.");

// ── Output DTOs (match the schema consumed by ManufacturerProvider / ModelCatalogProvider) ──
internal sealed class ManufacturerEntry
{
    public string Wmi { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Country { get; set; } = "";
    public string VehicleType { get; set; } = "";
    public List<string> Makes { get; set; } = [];
}

internal sealed class CatalogEntry
{
    public string Make { get; set; } = "";
    public string Wmi { get; set; } = "";
    public List<ModelEntry> Models { get; set; } = [];
}

internal sealed class ModelEntry
{
    public string Name { get; set; } = "";
    public int YearStart { get; set; }
    public int YearEnd { get; set; }
    public List<string> Packages { get; set; } = [];
    public Dictionary<string, string> Plants { get; set; } = [];
}

// ── Argument parsing ──────────────────────────────────────────────────────────
internal sealed class Args
{
    public string? OutputDir { get; private set; }
    public int YearStart { get; private set; } = 1996;
    public string[]? Makes { get; private set; }

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (int i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--output-dir" when i + 1 < argv.Length:
                    a.OutputDir = argv[++i];
                    break;
                case "--year-start" when i + 1 < argv.Length && int.TryParse(argv[++i], out var y):
                    a.YearStart = y;
                    break;
                case "--makes" when i + 1 < argv.Length:
                    a.Makes = argv[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
            }
        }
        return a;
    }
}
