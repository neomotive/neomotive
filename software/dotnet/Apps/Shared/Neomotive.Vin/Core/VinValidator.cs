using Neomotive.Vin.Contracts;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Core;

public sealed class VinValidator : IVinValidator
{
    private static readonly HashSet<char> ForbiddenLetters = ['I', 'O', 'Q'];

    public ValidationResult Validate(string vin)
    {
        if (string.IsNullOrWhiteSpace(vin))
            return ValidationResult.Fail("VIN cannot be empty.");

        vin = vin.ToUpperInvariant();
        var errors = new List<string>();

        if (vin.Length != 17)
            errors.Add($"VIN must be exactly 17 characters; got {vin.Length}.");

        for (int i = 0; i < vin.Length; i++)
        {
            char c = vin[i];
            if (ForbiddenLetters.Contains(c))
                errors.Add($"Illegal character '{c}' at position {i + 1} (I, O, Q are not valid VIN characters).");
            else if (!char.IsAsciiLetterOrDigit(c))
                errors.Add($"Invalid character '{c}' at position {i + 1}.");
        }

        if (errors.Count == 0 && !HasValidCheckDigit(vin))
            errors.Add("Check digit at position 9 is invalid.");

        return errors.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail([.. errors]);
    }

    public bool HasValidCheckDigit(string vin)
    {
        if (vin.Length != 17) return false;
        vin = vin.ToUpperInvariant();
        for (int i = 0; i < 17; i++)
            if (VinCharTable.GetCharValue(vin[i]) < 0) return false;
        return vin[8] == VinCharTable.ComputeCheckDigit(vin);
    }
}
