# Neomotive.Vin — Implementation Plan

## Phase 1 — Scaffold Projects

1. Create `Apps/Shared/Neomotive.Vin/Neomotive.Vin.csproj`
   - `net10.0`, nullable enable, implicit usings
   - Package refs: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Http`
   - Embedded resources: `Resources/manufacturers.json`, `Resources/model-catalog.json`

2. Create `Apps/Shared/Neomotive.Vin.Tests/Neomotive.Vin.Tests.csproj`
   - `net10.0`, xUnit, `Microsoft.NET.Test.Sdk`
   - Project ref → `Neomotive.Vin`
   - Moq or NSubstitute for mocking `INhtsaClient`

3. Add both projects to `neomotive.slnx`

---

## Phase 2 — Models

Files: `Models/ValidationResult.cs`, `Models/WmiInfo.cs`, `Models/VdsInfo.cs`,
`Models/VisInfo.cs`, `Models/VinDecodeResult.cs`, `Models/ManufacturerInfo.cs`,
`Models/VehicleModelInfo.cs`, `Models/GenerateVinRequest.cs`,
`Models/NhtsaDecodeResponse.cs`

All are `sealed record` types. No business logic in models.

---

## Phase 3 — Contracts (Interfaces)

Files: `Contracts/IVinValidator.cs`, `Contracts/IVinDecoder.cs`,
`Contracts/IVinGenerator.cs`, `Contracts/IManufacturerProvider.cs`,
`Contracts/INhtsaClient.cs`

---

## Phase 4 — Core Logic

### `Core/VinValidator.cs`
- Validate length == 17
- Validate each character is in `[A-Z0-9]` excl. `I`, `O`, `Q`
- Compute check digit, compare to position 9
- Returns `ValidationResult` with specific error messages per failure

### `Core/VinCharTable.cs` (internal static)
- Transliteration table: `A=1, B=2, C=3, D=4, E=5, F=6, G=7, H=8, J=1, K=2, L=3, M=4, N=5, P=7, R=9, S=2, T=3, U=4, V=5, W=6, X=7, Y=8, Z=9`
- Positional weights: `[8,7,6,5,4,3,2,10,0,9,8,7,6,5,4,3,2]`
- Model year decode table: `A=1980, B=1981, ...` cycling every 30 years

### `Core/VinDecoder.cs`
- `DecodeLocal`: split into WMI/VDS/VIS, lookup via `IManufacturerProvider` and `IModelCatalogProvider`
- `DecodeAsync`: call `DecodeLocal`, if incomplete call `INhtsaClient.DecodeAsync`, merge

### `Core/VinGenerator.cs`
- Implements `IVinGenerator`
- Catalog lookups via `IModelCatalogProvider`
- Encodes year to year-code char
- Assembles raw VIN, computes and injects check digit

---

## Phase 5 — Data Providers

### `Data/ManufacturerProvider.cs`
- Loads `manufacturers.json` from embedded resource on first call (`Lazy<T>`)
- Deserializes to `List<ManufacturerInfo>` via `System.Text.Json`
- Builds a `Dictionary<string, ManufacturerInfo>` keyed by WMI for O(1) lookup

### `Data/ModelCatalogProvider.cs`  (internal `IModelCatalogProvider`)
- Loads `model-catalog.json` similarly
- Exposes: `GetMakes()`, `GetModels(make)`, `GetYears(make, model)`,
  `GetPackages(make, model, year)`, `GetVdsTemplate(make, model, year, package)`

### `Resources/manufacturers.json`
- Seed data: GM (1G1, 1G6, 1GT), Ford (1FA, 1FB, 1FC, 1FD), Chrysler (1C3, 1C4, 2C3), 
  Toyota (JT2, JT3, JT4), Honda (1HG, 1HH), BMW (WBA, WBX),
  Mercedes (WDB, WDC), Volkswagen (WVW, WV2)

### `Resources/model-catalog.json`
- Seed data: Chevrolet Camaro, Silverado; Ford Mustang, F-150; Dodge Challenger, Ram 1500;
  Toyota Camry, Tundra; Honda Accord, Civic

---

## Phase 6 — NHTSA HTTP Client

### `Http/NhtsaClient.cs`
- Constructor: `HttpClient httpClient` (injected via `IHttpClientFactory`)
- `DecodeAsync(string vin)`: GET `vehicles/decodevin/{vin}?format=json`
- Deserialize `Results[]` array, map known field names to `NhtsaDecodeResponse`
- Return `null` on HTTP error or timeout; never throw to caller

### `Models/NhtsaDecodeResponse.cs`
- Flat record: `Make`, `Model`, `ModelYear`, `Manufacturer`, `VehicleType`, 
  `BodyClass`, `EngineCylinders`, `EngineModel`, `PlantCity`, `PlantCountry`

---

## Phase 7 — DI Registration

### `Extensions/ServiceCollectionExtensions.cs`
```csharp
public static IServiceCollection AddVinServices(
    this IServiceCollection services,
    Action<VinOptions>? configure = null)
```
- Bind `VinOptions`
- `AddSingleton<IVinValidator, VinValidator>()`
- `AddSingleton<IManufacturerProvider, ManufacturerProvider>()`
- `AddSingleton<IModelCatalogProvider, ModelCatalogProvider>()`
- `AddSingleton<IVinDecoder, VinDecoder>()`
- `AddSingleton<IVinGenerator, VinGenerator>()`
- `AddHttpClient<INhtsaClient, NhtsaClient>("nhtsa", client => { client.BaseAddress = opts.NhtsaBaseAddress; client.Timeout = opts.NhtsaTimeout; })`

---

## Phase 8 — Unit Tests

### `VinValidatorTests.cs`
- `Validate_ValidVin_ReturnsOk` — use a known real VIN
- `Validate_WrongLength_ReturnsError`
- `Validate_IllegalChar_ReturnsError` (I, O, Q)
- `Validate_BadCheckDigit_ReturnsError`
- `Validate_CheckDigitX_Valid` — VINs where check digit is X

### `VinDecoderTests.cs`
- `DecodeLocal_KnownGmVin_ReturnsCorrectMake`
- `DecodeLocal_UnknownWmi_ReturnsNullWmi`
- `DecodeAsync_NhtsaFallback_WhenLocalIncomplete` — mock `INhtsaClient`
- `DecodeAsync_NhtsaUnavailable_ReturnsPartialResult` — mock returns null
- `DecodeAsync_Cancellation_ThrowsOperationCancelled`

### `VinGeneratorTests.cs`
- `GenerateAsync_ValidRequest_ProducesValidCheckDigit`
- `GenerateAsync_Year2024_EncodesCorrectYearChar`
- `GenerateAsync_RoundTrip_DecodeMatchesRequest`
- `GetMakes_ReturnsSeededMakes`
- `GetPackages_FilteredByMakeModelYear`

### `NhtsaClientTests.cs`
- `DecodeAsync_200Response_MapsFields`
- `DecodeAsync_404_ReturnsNull`
- `DecodeAsync_Timeout_ReturnsNull`
- Uses `MockHttpMessageHandler` or `HttpClient` with fake handler

---

## Integration Points (callers to update after approval)

| Caller | Change |
|---|---|
| `ScanTool.Core` DI setup | Call `services.AddVinServices()` |
| `ModuleSimulator.Core` DI setup | Call `services.AddVinServices()` |
| `SimulatorState` | Replace hardcoded `"AWWWWWWWWWWW0YEAH"` with `IVinGenerator.GenerateAsync(...)` call at startup |
| `ScanTool` Vehicle view | Feed decoded `VinDecodeResult` fields into VM for richer display |

---

## File Count Summary

| Category | Count |
|---|---|
| Contracts (interfaces) | 5 |
| Models | 9 |
| Core logic | 3 |
| Data providers | 2 |
| HTTP client | 1 |
| DI / Options | 2 |
| JSON resource files | 2 |
| Test classes | 4 |
| **Total** | **28** |
