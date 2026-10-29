using System.Text.RegularExpressions;

namespace CustomerSupportAI.Models;

public static partial class PhoneNumber
{
    /// <summary>Canonical Vietnamese form ("0912345678"), or null when the input is not a phone number.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = Separators().Replace(input.Trim(), "");
        if (digits.StartsWith("+84")) digits = "0" + digits[3..];
        else if (digits.StartsWith("84") && digits.Length >= 11) digits = "0" + digits[2..];
        return Local().IsMatch(digits) ? digits : null;
    }

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^0\d{9,10}$")]
    private static partial Regex Local();
}
