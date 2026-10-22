using System.Globalization;
using System.Text.Json;
using ADM.Domain;

namespace ADM.Integrations.Gsx;

/// <summary>Installed Remote API guide §8.13: feature-detected, read-only handler proxy data.</summary>
public static class GsxFlightReader
{
    public static string? Text(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
    public static string? AircraftType(JsonElement handler)
    {
        if (handler.ValueKind != JsonValueKind.Object || !handler.TryGetProperty("aircraft", out var aircraft)) return null;
        string? type = Text(aircraft, "icaoTypeDesignator") ?? Text(aircraft, "icaoType");
        return type is { Length: >= 2 and <= 4 } && type.All(char.IsAsciiLetterOrDigit) ? type.ToUpperInvariant() : null;
    }
    public static string? SetupKey(JsonElement handler)
    {
        if (AircraftType(handler) is not { } type || !handler.TryGetProperty("aircraft", out var aircraft)) return null;
        string? identity = Text(aircraft, "handlerName") ?? Text(aircraft, "title");
        return string.IsNullOrWhiteSpace(identity) || identity.Length > 160 ? null : type + ":" + identity;
    }
    public static string? CurrentAirport(JsonElement handler)
    {
        if (handler.ValueKind != JsonValueKind.Object || !handler.TryGetProperty("airport", out var airport)) return null;
        string? icao = Text(airport, "icao")?.ToUpperInvariant(); return Airport(icao) ? icao : null;
    }
    public static string? CurrentStand(JsonElement handler) => handler.ValueKind == JsonValueKind.Object && handler.TryGetProperty("gate", out var gate) ? Text(gate, "uiName") : null;
    public static FlightPlanSnapshot? Read(JsonElement handler, DateTimeOffset observedAt)
    {
        if (handler.ValueKind != JsonValueKind.Object || !handler.TryGetProperty("simbrief", out var plan) || plan.ValueKind != JsonValueKind.Object ||
            !handler.TryGetProperty("aircraft", out var aircraft) || AircraftType(handler) is not { } type) return null;
        string? callsign = Text(plan, "callsign")?.ToUpperInvariant(), origin = Text(plan, "originICAO")?.ToUpperInvariant(), destination = Text(plan, "destinationICAO")?.ToUpperInvariant();
        if (callsign is not { Length: > 0 and <= 16 } || !callsign.All(char.IsAsciiLetterOrDigit) || !Airport(origin) || !Airport(destination) || origin == destination ||
            !string.IsNullOrWhiteSpace(Text(plan, "last_error"))) return null;
        if (!plan.TryGetProperty("sched_out", out var schedule)) return null;
        DateTimeOffset scheduled;
        try
        {
            if (schedule.ValueKind == JsonValueKind.Array && schedule.GetArrayLength() >= 6)
                scheduled = new(schedule[0].GetInt32(), schedule[1].GetInt32(), schedule[2].GetInt32(), schedule[3].GetInt32(), schedule[4].GetInt32(), schedule[5].GetInt32(), TimeSpan.Zero);
            else if (schedule.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(schedule.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) scheduled = parsed;
            else return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException) { return null; }
        if (observedAt - scheduled > TimeSpan.FromHours(24) || scheduled - observedAt > TimeSpan.FromHours(24)) return null;
        // Handler Weight values are kilograms (§8.13), independent of display unitsName.
        double? Weight(string key) => plan.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double weight) && double.IsFinite(weight) && weight is >= 0 and <= 1_000_000 ? weight : null;
        int? passengers = plan.TryGetProperty("passengers", out var pax) && pax.ValueKind == JsonValueKind.Number && pax.TryGetInt32(out int count) && count is >= 0 and <= 1000 ? count : null;
        return new(callsign, origin!, destination!, new(type, null), observedAt)
        {
            Source = FlightPlanSource.Gsx,
            SourceAircraftTitle = Text(aircraft, "title"),
            ScheduledDepartureUtc = scheduled,
            PlannedPassengers = passengers,
            PlannedRampFuelKg = Weight("plan_ramp"),
            PlannedZeroFuelWeightKg = Weight("zfw")
        };
    }
    private static bool Airport(string? value) => value is { Length: 4 } && value.All(char.IsAsciiLetterOrDigit);
}
