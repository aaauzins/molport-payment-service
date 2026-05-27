using System.Text.RegularExpressions;

namespace PaymentService.Services;

public static class ReferenceExtractor
{
    // Matches standalone numbers that look like order/invoice references (5+ digits)
    private static readonly Regex Pattern = new(@"\b(\d{5,})\b", RegexOptions.Compiled);

    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Pattern.Match(text);
        return match.Success ? match.Value : null;
    }
}
