# Neomotive.Vin — Architecture Document

## Overview

A production-grade VIN decode/generate library living in `Apps/Shared/`, consumed by both **ScanTool** and **ModuleSimulator**.  Follows the same conventions as `Neomotive.Obd2`: net10.0, nullable enabled, implicit usings, embedded JSON resources, no UI dependencies.

---

## VIN Structure Reference

```
Position:  1  2  3 | 4  5  6  7  8 | 9 | 10 | 11 | 12 13 14 15 16 17
Section:   W  M  I   V  D  S         CD   MY   Plant   V  I  S
```

| Section | Positions | Length | Meaning |
|---------|-----------|--------|---------|
| WMI | 1–3 | 3 | World Manufacturer Identifier |
| VDS | 4–8 | 5 | Vehicle Descriptor Section (make/model/body/engine) |
| Check digit | 9 | 1 | ISO 3779 modulo-11 check (0–9 or X) |
| Model year | 10 | 1 | Year code (A–Y skipping I/O/Q/U/Z, then 1–9, cycles) |
| Plant | 11 | 1 | Assembly plant (manufacturer-defined) |
| VIS sequence | 12–17 | 6 | Production sequence number |

**Valid characters**: A–Z (excl. I, O, Q) and 0–9  
**Check digit algorithm**: transliterate each char to a value (0–9), multiply by positional weight `[8,7,6,5,4,3,2,10,0,9,8,7,6,5,4,3,2]`, sum all products mod 11; 10 → `X`.

---

## Project Layout

```
Apps/Shared/
├── Neomotive.Vin/                     ← library (this new project)
│   ├── Contracts/
│   │   ├── IVinDecoder.cs
│   │   ├── IVinGenerator.cs
│   │   ├── IVinValidator.cs
│   │   ├── IManufacturerProvider.cs
│   │   └── INhtsaClient.cs
│   ├── Models/
│   │   ├── VinDecodeResult.cs
│   │   ├── VinSection.cs              (WmiInfo, VdsInfo, VisInfo)
│   │   ├── ValidationResult.cs
│   │   ├── ManufacturerInfo.cs
│   │   ├── VehicleModelInfo.cs
│   │   └── GenerateVinRequest.cs
│   ├── Core/
│   │   ├── VinValidator.cs
│   │   ├── VinDecoder.cs
│   │   └── VinGenerator.cs
│   ├── Data/
│   │   ├── ManufacturerProvider.cs    (loads manufacturers.json)
│   │   └── ModelCatalogProvider.cs    (loads model-catalog.json)
│   ├── Http/
│   │   └── NhtsaClient.cs             (HttpClient wrapper, injected)
│   ├── Resources/
│   │   ├── manufacturers.json
│   │   └── model-catalog.json
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs   (DI wiring)
│   └── Neomotive.Vin.csproj
│
└── Neomotive.Vin.Tests/               ← xUnit test project
    ├── VinValidatorTests.cs
    ├── VinDecoderTests.cs
    ├── VinGeneratorTests.cs
    ├── NhtsaClientTests.cs
    └── Neomotive.Vin.Tests.csproj
```

---

## Interfaces

### `IVinValidator`
```csharp
public interface IVinValidator
{
    ValidationResult Validate(string vin);
    bool HasValidCheckDigit(string vin);
}
```

### `IVinDecoder`
```csharp
public interface IVinDecoder
{
    // Local-only decode (no network). Always synchronous.
    VinDecodeResult DecodeLocal(string vin);

    // Full decode: local first, NHTSA fallback if local data is incomplete.
    Task<VinDecodeResult> DecodeAsync(string vin, CancellationToken cancellationToken = default);
}
```

### `IVinGenerator`
```csharp
public interface IVinGenerator
{
    // Builds a valid VIN from explicit parameters. Computes check digit automatically.
    Task<string> GenerateAsync(GenerateVinRequest request, CancellationToken cancellationToken = default);

    // Returns all available makes in the catalog.
    Task<IReadOnlyList<string>> GetMakesAsync(CancellationToken cancellationToken = default);

    // Returns models for a given make.
    Task<IReadOnlyList<string>> GetModelsAsync(string make, CancellationToken cancellationToken = default);

    // Returns years for a given make + model.
    Task<IReadOnlyList<int>> GetYearsAsync(string make, string model, CancellationToken cancellationToken = default);

    // Returns packages/trims for a given make + model + year.
    Task<IReadOnlyList<string>> GetPackagesAsync(string make, string model, int year, CancellationToken cancellationToken = default);
}
```

### `IManufacturerProvider`
```csharp
public interface IManufacturerProvider
{
    Task<ManufacturerInfo?> GetByWmiAsync(string wmi, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManufacturerInfo>> GetAllAsync(CancellationToken cancellationToken = default);
}
```

### `INhtsaClient`
```csharp
public interface INhtsaClient
{
    Task<NhtsaDecodeResponse?> DecodeAsync(string vin, CancellationToken cancellationToken = default);
}
```

---

## Models

### `ValidationResult`
```csharp
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok() => new(true, []);
    public static ValidationResult Fail(params string[] errors) => new(false, errors);
}
```

### `VinDecodeResult`
```csharp
public sealed record VinDecodeResult
{
    public required string Vin { get; init; }
    public required ValidationResult Validation { get; init; }
    public WmiInfo? Wmi { get; init; }
    public VdsInfo? Vds { get; init; }
    public VisInfo? Vis { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public int? Year { get; init; }
    public string? Trim { get; init; }
    public string? EngineType { get; init; }
    public string? Country { get; init; }
    public string? PlantCity { get; init; }
    public bool IsFromNhtsa { get; init; }
}
```

### `WmiInfo`
```csharp
public sealed record WmiInfo(string Code, string Manufacturer, string Country, string VehicleType);
```

### `VdsInfo`
```csharp
public sealed record VdsInfo(string Code, string? ModelCode, string? BodyStyle, string? EngineCode, string? RestraintSystem);
```

### `VisInfo`
```csharp
public sealed record VisInfo(char YearCode, int ModelYear, char PlantCode, string SequenceNumber);
```

### `ManufacturerInfo`
```csharp
public sealed record ManufacturerInfo
{
    public required string Wmi { get; init; }
    public required string Manufacturer { get; init; }
    public required string Country { get; init; }
    public required string VehicleType { get; init; }
    public IReadOnlyList<VehicleModelInfo> Models { get; init; } = [];
}
```

### `GenerateVinRequest`
```csharp
public sealed record GenerateVinRequest
{
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required int Year { get; init; }
    public string? Package { get; init; }
    public string? SequenceNumber { get; init; }   // null = random 6-digit
}
```

---

## JSON Schema Designs

### `Resources/manufacturers.json`
```json
[
  {
    "wmi": "1G1",
    "manufacturer": "General Motors",
    "country": "United States",
    "vehicleType": "Passenger Car",
    "makes": ["Chevrolet"]
  }
]
```

**Schema** (abbreviated):
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `wmi` | string(3) | yes | Uppercase, alphanumeric |
| `manufacturer` | string | yes | Legal entity name |
| `country` | string | yes | Full country name |
| `vehicleType` | string | yes | Passenger Car, Truck, MPV, etc. |
| `makes` | string[] | yes | Brand names this WMI covers |

### `Resources/model-catalog.json`
```json
[
  {
    "make": "Chevrolet",
    "wmi": "1G1",
    "models": [
      {
        "name": "Camaro",
        "yearStart": 2010,
        "yearEnd": 2024,
        "packages": ["LS", "LT", "SS", "ZL1"],
        "vdsTemplate": {
          "modelCode": "F",
          "bodyStyle": "2",
          "engineCodes": { "LS": "C", "LT": "C", "SS": "E", "ZL1": "G" },
          "restraintCode": "7"
        },
        "plants": { "B": "Bowling Green, KY" }
      }
    ]
  }
]
```

**Schema** (abbreviated):
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `make` | string | yes | Brand name |
| `wmi` | string(3) | yes | Links to manufacturers.json |
| `models[].name` | string | yes | Model name |
| `models[].yearStart` | int | yes | First model year |
| `models[].yearEnd` | int | yes | Last model year (or 9999 for current) |
| `models[].packages` | string[] | yes | Trim/package codes |
| `models[].vdsTemplate` | object | yes | Per-package VDS byte mapping |
| `models[].plants` | object | no | Plant code → city |

---

## Dependency Injection Wiring

`ServiceCollectionExtensions.AddVinServices(this IServiceCollection services, Action<VinOptions>? configure = null)`

Registers:
- `IVinValidator` → `VinValidator` (singleton, stateless)
- `IManufacturerProvider` → `ManufacturerProvider` (singleton, lazy-loaded JSON)
- `IVinDecoder` → `VinDecoder` (singleton)
- `IVinGenerator` → `VinGenerator` (singleton)
- `INhtsaClient` → `NhtsaClient` (scoped, uses `IHttpClientFactory`)
- `IHttpClientFactory` (via `AddHttpClient()`) with named client `"nhtsa"` and base address

### `VinOptions`
```csharp
public sealed class VinOptions
{
    public Uri NhtsaBaseAddress { get; set; } = new("https://vpic.nhtsa.dot.gov/api/");
    public bool EnableNhtsaFallback { get; set; } = true;
    public TimeSpan NhtsaTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
```

---

## NHTSA API Integration

**Endpoint**: `GET /api/vehicles/decodevin/{vin}?format=json`

- Called only when local decode is incomplete (no matching WMI or model found)
- Response mapped to `NhtsaDecodeResponse` and merged into `VinDecodeResult` with `IsFromNhtsa = true`
- `INhtsaClient` is the seam for unit testing (can be replaced with a mock)
- `HttpClient` is registered via `IHttpClientFactory` (retries via Polly if desired in the future)

---

## Decode Flow

```
DecodeAsync(vin)
  │
  ├─ IVinValidator.Validate(vin)          ← fail fast on format/check-digit errors
  │
  ├─ IManufacturerProvider.GetByWmiAsync  ← vin[0..3]
  │
  ├─ Local VDS/VIS decode                 ← model-catalog lookup
  │
  ├─ result.IsComplete?
  │     YES → return result
  │     NO  → INhtsaClient.DecodeAsync(vin)
  │               └─ merge NHTSA fields into result
  │
  └─ return VinDecodeResult
```

---

## Generate Flow

```
GenerateAsync(request)
  │
  ├─ Look up WMI from model-catalog (make → wmi)
  ├─ Look up VDS template for model + year + package
  ├─ Encode model year → year code char (position 10)
  ├─ Select plant code (position 11, first in list or random)
  ├─ Build sequence number string (positions 12–17, padded/random)
  ├─ Assemble 17-char string with '0' at position 9
  ├─ Compute and inject check digit at position 9
  └─ return VIN string
```

---

## Testing Strategy

| Test class | What it covers |
|---|---|
| `VinValidatorTests` | Length, illegal chars, check digit (valid/invalid/X), edge cases |
| `VinDecoderTests` | Known VINs, WMI lookup hit/miss, NHTSA fallback path, cancellation |
| `VinGeneratorTests` | Round-trip (generate → decode matches request params), check digit validity |
| `NhtsaClientTests` | Happy path, 4xx, timeout, deserialization via `MockHttpMessageHandler` |

All tests use xUnit. `INhtsaClient` is mocked so tests are offline. JSON files are loaded from embedded resources (no file I/O in tests).

---

## What Does NOT Need Code Changes to Add a Manufacturer

1. Add a WMI entry to `manufacturers.json`
2. Add make/model entries to `model-catalog.json`
3. Rebuild — no C# changes required

This satisfies the requirement that new manufacturers can be added without code changes.
