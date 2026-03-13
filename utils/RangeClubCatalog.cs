using System;
using System.Collections.Generic;
using System.Text;

public static class RangeClubCatalog
{
    public const string DefaultClubLabel = "DRIVER";

    private static readonly string[] ClubLabels =
    {
        "DRIVER", "3W", "5W", "4H",
        "3I", "4I", "5I", "6I", "7I", "8I", "9I",
        "PW", "GW", "SW", "LW"
    };

    private static readonly HashSet<string> ClubSet = new(ClubLabels, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Labels => ClubLabels;

    public static string NormalizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return DefaultClubLabel;

        string upper = label.Trim().ToUpperInvariant();
        return ClubSet.Contains(upper) ? upper : DefaultClubLabel;
    }

    public static string ToFileTag(string label)
    {
        string normalized = NormalizeLabel(label);
        var builder = new StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.Length == 0 ? "driver" : builder.ToString();
    }
}
