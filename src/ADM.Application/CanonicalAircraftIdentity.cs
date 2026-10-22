using System.Text.RegularExpressions;
using ADM.Domain;

namespace ADM.Application;

public static partial class CanonicalAircraftIdentity
{
    public static AircraftIdentity Create(string? verifiedIcaoType, string? family,
        IReadOnlyDictionary<string, string>? explicitAliases = null)
    {
        string? type = verifiedIcaoType?.Trim().ToUpperInvariant();
        if (explicitAliases is not null && verifiedIcaoType is not null && explicitAliases.TryGetValue(verifiedIcaoType, out var mapped)) type = mapped.Trim().ToUpperInvariant();
        if (type is null || !TypePattern().IsMatch(type)) type = null;
        string? canonicalFamily = family?.Trim().ToUpperInvariant();
        if (canonicalFamily is null || !FamilyPattern().IsMatch(canonicalFamily)) canonicalFamily = null;
        return new(type, canonicalFamily);
    }
    [GeneratedRegex("^[A-Z0-9]{2,4}$", RegexOptions.CultureInvariant)] private static partial Regex TypePattern();
    [GeneratedRegex("^[A-Z0-9_]{2,24}$", RegexOptions.CultureInvariant)] private static partial Regex FamilyPattern();
}
