namespace Neomotive.Vin.Core;

internal static class VinCharTable
{
    internal static readonly int[] Weights = [8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2];

    private static readonly Dictionary<char, int> CharValues = new()
    {
        ['A'] = 1, ['B'] = 2, ['C'] = 3, ['D'] = 4, ['E'] = 5,
        ['F'] = 6, ['G'] = 7, ['H'] = 8,
        ['J'] = 1, ['K'] = 2, ['L'] = 3, ['M'] = 4, ['N'] = 5,
        ['P'] = 7, ['R'] = 9,
        ['S'] = 2, ['T'] = 3, ['U'] = 4, ['V'] = 5, ['W'] = 6,
        ['X'] = 7, ['Y'] = 8, ['Z'] = 9,
        ['0'] = 0, ['1'] = 1, ['2'] = 2, ['3'] = 3, ['4'] = 4,
        ['5'] = 5, ['6'] = 6, ['7'] = 7, ['8'] = 8, ['9'] = 9
    };

    // 30-value cycle starting at 1980: A-Y skipping I,O,Q,U,Z, then 1-9
    internal static readonly char[] YearCodes =
        ['A','B','C','D','E','F','G','H','J','K','L','M','N','P','R','S','T','V','W','X','Y',
         '1','2','3','4','5','6','7','8','9'];

    internal static bool IsValidVinChar(char c) => CharValues.ContainsKey(c);

    internal static int GetCharValue(char c) => CharValues.TryGetValue(c, out var v) ? v : -1;

    internal static char ComputeCheckDigit(string vin)
    {
        int sum = 0;
        for (int i = 0; i < 17; i++)
            sum += GetCharValue(vin[i]) * Weights[i];
        int r = sum % 11;
        return r == 10 ? 'X' : (char)('0' + r);
    }

    internal static char EncodeModelYear(int year)
    {
        int idx = (year - 1980) % 30;
        if (idx < 0) idx += 30;
        return YearCodes[idx];
    }

    internal static int DecodeModelYear(char code)
    {
        int idx = Array.IndexOf(YearCodes, code);
        if (idx < 0) return 0;
        // Same code recurs every 30 years; pick the most recent year not more than 1 year in the future.
        int baseYear = 1980 + idx;
        int currentYear = DateTime.UtcNow.Year;
        while (baseYear + 30 <= currentYear + 1)
            baseYear += 30;
        return baseYear;
    }
}
