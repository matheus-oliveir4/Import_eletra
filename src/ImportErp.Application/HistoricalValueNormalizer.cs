using System.Text;

namespace ImportErp.Application;

/// <summary>
/// Applies only formatting rules that do not resolve business ambiguity. Raw
/// workbook values stay in staging for audit and comparison.
/// </summary>
public static class HistoricalValueNormalizer
{
    private static readonly HashSet<string> ScalarFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PO Totvs", "IP Number", "Importer", "Currency", "Freight Ccy.", "Status", "NCM", "Product Code"
    };

    private static readonly IReadOnlyDictionary<string, string> ImporterNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ELETRA MATRIZ"] = "ELETRA MATRIZ",
        ["ELETRA FOR"] = "ELETRA FOR",
        ["ELETRA CWB"] = "ELETRA CWB"
    };

    public static bool IsScalarField(string fieldName) => ScalarFields.Contains(fieldName);

    public static bool ContainsMultipleValues(string fieldName, string value)
    {
        if (!IsScalarField(fieldName)) return false;
        return value.Contains('\n') || value.Contains('\r') || value.Contains(';') || value.Contains('|');
    }

    public static string? Normalize(string fieldName, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;
        var normalized = rawValue.Normalize(NormalizationForm.FormKC)
            .Replace('\u00A0', ' ')
            .Replace("\u200B", string.Empty, StringComparison.Ordinal)
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal);
        normalized = string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0 || ContainsMultipleValues(fieldName, normalized)) return null;

        if (fieldName.Equals("Importer", StringComparison.OrdinalIgnoreCase))
        {
            return ImporterNames.TryGetValue(normalized, out var canonical) ? canonical : normalized;
        }

        if (fieldName.Equals("PO Totvs", StringComparison.OrdinalIgnoreCase)
            || fieldName.Equals("IP Number", StringComparison.OrdinalIgnoreCase)
            || fieldName.Equals("Currency", StringComparison.OrdinalIgnoreCase)
            || fieldName.Equals("Freight Ccy.", StringComparison.OrdinalIgnoreCase)
            || fieldName.Equals("Status", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.ToUpperInvariant();
        }

        return normalized;
    }
}
