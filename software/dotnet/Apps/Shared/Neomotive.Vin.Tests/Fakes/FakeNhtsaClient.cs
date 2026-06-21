using Neomotive.Vin.Contracts;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Tests.Fakes;

internal sealed class FakeNhtsaClient : INhtsaClient
{
    private readonly NhtsaDecodeResponse? _response;
    public int CallCount { get; private set; }

    public FakeNhtsaClient(NhtsaDecodeResponse? response = null) => _response = response;

    public Task<NhtsaDecodeResponse?> DecodeAsync(string vin, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(_response);
    }
}
