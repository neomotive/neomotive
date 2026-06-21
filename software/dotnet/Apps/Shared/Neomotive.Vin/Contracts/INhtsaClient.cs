using Neomotive.Vin.Models;

namespace Neomotive.Vin.Contracts;

public interface INhtsaClient
{
    Task<NhtsaDecodeResponse?> DecodeAsync(string vin, CancellationToken cancellationToken = default);
}
