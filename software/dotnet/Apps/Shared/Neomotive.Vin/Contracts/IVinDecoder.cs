using Neomotive.Vin.Models;

namespace Neomotive.Vin.Contracts;

public interface IVinDecoder
{
    VinDecodeResult DecodeLocal(string vin);
    Task<VinDecodeResult> DecodeAsync(string vin, CancellationToken cancellationToken = default);
}
