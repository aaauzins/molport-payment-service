using System.Text.RegularExpressions;

namespace PaymentService.Services;

public static class ReferenceExtractor
{
    // Matches Molport order/invoice number format:
    // 2 letters (year+month) + 2 digits (day) + 1 letter (hour) + 7 digits (min+sec+ms)
    // optionally followed by an invoice suffix like -I1 or -O1
    // Example: YF27O3045123 or YF27O3045123-I1
    private static readonly Regex Pattern = new(
        @"[A-Z]{2}\d{2}[A-Z]\d{7}(?:-[A-Z]\d+)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Pattern.Match(text);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }
}
