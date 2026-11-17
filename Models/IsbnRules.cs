namespace LibraryManagementSystem.Models;

public static class IsbnRules
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = value.Trim();
        if (normalized.StartsWith("ISBN", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..].TrimStart(' ', ':', '-');
        }

        return string.Concat(normalized.Where(character => character != '-' && !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
    }

    public static bool IsValid(string? value)
    {
        var isbn = Normalize(value);
        if (isbn is null) return true;

        if (isbn.Length == 10)
        {
            var sum = 0;
            for (var index = 0; index < 9; index++)
            {
                if (isbn[index] is < '0' or > '9') return false;
                sum += (isbn[index] - '0') * (10 - index);
            }

            var check = isbn[9] == 'X' ? 10 :
                isbn[9] is >= '0' and <= '9' ? isbn[9] - '0' : -1;
            return check >= 0 && (sum + check) % 11 == 0;
        }

        if (isbn.Length != 13 || isbn.Any(character => character is < '0' or > '9'))
            return false;

        var weightedSum = 0;
        for (var index = 0; index < 12; index++)
            weightedSum += (isbn[index] - '0') * (index % 2 == 0 ? 1 : 3);

        var expectedCheckDigit = (10 - weightedSum % 10) % 10;
        return isbn[12] - '0' == expectedCheckDigit;
    }
}
