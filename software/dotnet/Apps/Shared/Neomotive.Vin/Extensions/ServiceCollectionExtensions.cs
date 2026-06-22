using Microsoft.Extensions.DependencyInjection;
using Neomotive.Vin.Contracts;
using Neomotive.Vin.Core;
using Neomotive.Vin.Data;
using Neomotive.Vin.Http;

namespace Neomotive.Vin.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVinServices(
        this IServiceCollection services,
        Action<VinOptions>? configure = null)
    {
        var options = new VinOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IVinValidator, VinValidator>();
        services.AddSingleton<IManufacturerProvider>(sp => new ManufacturerProvider(sp.GetRequiredService<VinOptions>()));
        services.AddSingleton<IModelCatalogProvider>(sp => new ModelCatalogProvider(sp.GetRequiredService<VinOptions>()));
        services.AddSingleton<IVinDecoder, VinDecoder>();
        services.AddSingleton<IVinGenerator, VinGenerator>();

        services.AddHttpClient<INhtsaClient, NhtsaClient>("nhtsa", client =>
        {
            client.BaseAddress = options.NhtsaBaseAddress;
            client.Timeout = options.NhtsaTimeout;
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        return services;
    }
}
