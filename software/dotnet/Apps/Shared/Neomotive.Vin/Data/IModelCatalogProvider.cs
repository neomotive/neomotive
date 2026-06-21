using Neomotive.Vin.Models;

namespace Neomotive.Vin.Data;

public interface IModelCatalogProvider
{
    IReadOnlyList<string> GetMakes();
    IReadOnlyList<string> GetModels(string make);
    IReadOnlyList<int> GetYears(string make, string model);
    IReadOnlyList<string> GetPackages(string make, string model, int year);
    VehicleModelInfo? GetModel(string make, string model);
    string? GetWmiForMake(string make);
}
