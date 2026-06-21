using Neomotive.Vin.Models;

namespace Neomotive.Vin.Contracts;

public interface IVinValidator
{
    ValidationResult Validate(string vin);
    bool HasValidCheckDigit(string vin);
}
