using System.Collections.Frozen;
using System.Text.RegularExpressions;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application;

/// <summary>Offline identity syntax and catalogue validation; never guesses from commercial flight numbers.</summary>
public sealed partial class AirlineIdentityResolver(IEnumerable<string> validatedDesignators,
    IReadOnlyDictionary<string, string>? aliases = null) : IAirlineIdentityResolver, IUserAirlineRegistry
{
    private readonly FrozenSet<string> _designators = validatedDesignators
        .Select(x => x.Trim().ToUpperInvariant()).Where(x => DesignatorPattern().IsMatch(x))
        .ToFrozenSet(StringComparer.Ordinal);
    private readonly FrozenDictionary<string, string> _aliases = (aliases ?? new Dictionary<string, string>())
        .ToFrozenDictionary(pair => pair.Key.Trim().ToUpperInvariant(), pair => pair.Value.Trim().ToUpperInvariant(), StringComparer.Ordinal);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _profileDesignators = new(StringComparer.Ordinal);
    // An explicitly created/imported local profile is a user-maintained catalogue entry, not a guessed carrier.
    public void RegisterProfileDesignator(string icao)
    {
        if (icao == "GENERIC") return;
        if (!DesignatorPattern().IsMatch(icao)) throw new ArgumentException("Use a three-letter uppercase ICAO code.");
        _profileDesignators.TryAdd(icao, 0);
    }
    public AirlineResolution Resolve(FlightIdentity identity, UserAirlineOverride? manualOverride)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string? explicitCode = string.Equals(manualOverride?.Icao.Trim(), "GENERIC", StringComparison.OrdinalIgnoreCase) ? "GENERIC" : Validate(manualOverride?.Icao);
        if (manualOverride is not null && explicitCode is null)
            throw new ArgumentException("Flight override must name a validated ICAO designator.", nameof(manualOverride));

        string? brief = Parse(identity.SimBriefAtcCallsign);
        string? simulator = identity.SimulatorCallsignVerified ? Parse(identity.SimulatorAtcCallsign) : null;
        bool conflict = brief is not null && simulator is not null && brief != simulator;
        (string? code, AirlineSource source) = explicitCode is not null ? (explicitCode, AirlineSource.FlightOverride)
            : brief is not null ? (brief, AirlineSource.SimBriefAtcCallsign)
            : simulator is not null ? (simulator, AirlineSource.SimulatorCallsign)
            : (Validate(identity.ManualAirline), AirlineSource.ManualSelection);
        if (code == "GENERIC") return new("GENERIC", source, null, false, conflict, "Explicit generic preferences; no carrier was inferred.");
        return code is null
            ? new("GENERIC", AirlineSource.Generic, null, false, conflict, "No validated ICAO designator; generic preferences apply.")
            : new(code, source, RawDesignator(source == AirlineSource.SimBriefAtcCallsign ? identity.SimBriefAtcCallsign :
                source == AirlineSource.SimulatorCallsign ? identity.SimulatorAtcCallsign : code), true, conflict,
                "Validated against the configured offline catalogue or explicit alias; operating carrier may be overridden.");
    }

    private string? Parse(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return null;
        string normalized = string.Concat(callsign.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        Match match = CallsignPattern().Match(normalized);
        return match.Success ? Validate(match.Groups[1].Value) : null;
    }

    private string? Validate(string? value)
    {
        string? code = value?.Trim().ToUpperInvariant();
        if (code is not null && (_designators.Contains(code) || _profileDesignators.ContainsKey(code))) return code;
        return code is not null && DesignatorPattern().IsMatch(code) && _aliases.TryGetValue(code, out var target) && _designators.Contains(target) ? target : null;
    }
    private static string? RawDesignator(string? callsign)
    {
        string normalized = string.Concat((callsign ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return normalized.Length >= 3 ? normalized[..3] : null;
    }

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex DesignatorPattern();
    [GeneratedRegex("^([A-Z]{3})([0-9][A-Z0-9]{0,6})$", RegexOptions.CultureInvariant)]
    private static partial Regex CallsignPattern();
}
