using Neomotive.Vin.Contracts;
using Neomotive.Vin.Extensions;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Core;

public sealed class VinDecoder : IVinDecoder
{
    private readonly IVinValidator _validator;
    private readonly IManufacturerProvider _manufacturers;
    private readonly INhtsaClient _nhtsa;
    private readonly bool _nhtsaEnabled;

    public VinDecoder(
        IVinValidator validator,
        IManufacturerProvider manufacturers,
        INhtsaClient nhtsa,
        VinOptions options)
    {
        _validator = validator;
        _manufacturers = manufacturers;
        _nhtsa = nhtsa;
        _nhtsaEnabled = options.EnableNhtsaFallback;
    }

    public VinDecodeResult DecodeLocal(string vin)
    {
        vin = (vin ?? "").ToUpperInvariant();
        var validation = _validator.Validate(vin);

        if (vin.Length != 17)
            return new VinDecodeResult { Vin = vin, Validation = validation };

        var wmiCode = vin[..3];
        var vdsCode = vin[3..8];
        var yearCode = vin[9];
        var plantCode = vin[10];
        var sequence = vin[11..];

        int modelYear = VinCharTable.DecodeModelYear(yearCode);
        var vis = new VisInfo(yearCode, modelYear, plantCode, sequence);
        var vds = new VdsInfo(vdsCode, null, null, null, null);

        // ManufacturerProvider is backed by Task.FromResult — safe to unwrap synchronously.
        var mfr = _manufacturers.GetByWmiAsync(wmiCode).GetAwaiter().GetResult();
        WmiInfo? wmiInfo = mfr is not null
            ? new WmiInfo(wmiCode, mfr.Manufacturer, mfr.Country, mfr.VehicleType)
            : null;

        return new VinDecodeResult
        {
            Vin = vin,
            Validation = validation,
            Wmi = wmiInfo,
            Vds = vds,
            Vis = vis,
            Make = mfr?.Makes.FirstOrDefault(),
            Year = modelYear > 0 ? modelYear : null,
            Country = mfr?.Country,
            IsFromNhtsa = false
        };
    }

    public async Task<VinDecodeResult> DecodeAsync(string vin, CancellationToken cancellationToken = default)
    {
        var local = DecodeLocal(vin);

        if (!local.Validation.IsValid || !_nhtsaEnabled)
            return local;

        // Already have enough local data — skip the network call
        if (local.Wmi is not null && local.Model is not null)
            return local;

        var nhtsa = await _nhtsa.DecodeAsync(vin, cancellationToken).ConfigureAwait(false);
        if (nhtsa is null)
            return local;

        WmiInfo? mergedWmi = local.Wmi ?? (nhtsa.Manufacturer is not null
            ? new WmiInfo(vin[..3], nhtsa.Manufacturer, nhtsa.PlantCountry ?? "", nhtsa.VehicleType ?? "")
            : null);

        return new VinDecodeResult
        {
            Vin = local.Vin,
            Validation = local.Validation,
            Wmi = mergedWmi,
            Vds = local.Vds,
            Vis = local.Vis,
            Make = local.Make ?? nhtsa.Make,
            Model = local.Model ?? nhtsa.Model,
            Year = local.Year ?? nhtsa.ModelYear,
            Trim = nhtsa.Trim,
            EngineType = nhtsa.EngineModel,
            PlantCity = local.PlantCity ?? nhtsa.PlantCity,
            Country = local.Country ?? nhtsa.PlantCountry,
            IsFromNhtsa = true
        };
    }
}
