using System.Collections.Immutable;

namespace ADM.Domain;

public sealed record RealismOptions(bool Enabled = true, int ProbabilityPercent = 25, int MaximumDelayMinutes = 10)
{
    public static RealismOptions Disabled { get; } = new(false, 0, 0);
    public void Validate()
    {
        if (ProbabilityPercent is < 0 or > 100 || MaximumDelayMinutes is < 0 or > 10) throw new ArgumentException("Realism probability must be 0–100% and the maximum delay 0–10 minutes.");
    }
}
public sealed record RealismTuning(int? ProbabilityPercent = null, int? MaximumDelayMinutes = null)
{
    public RealismOptions Apply(RealismOptions global)
    {
        var result = global with
        {
            ProbabilityPercent = ProbabilityPercent ?? global.ProbabilityPercent,
            MaximumDelayMinutes = Math.Min(global.MaximumDelayMinutes, MaximumDelayMinutes ?? global.MaximumDelayMinutes)
        };
        result.Validate(); return result;
    }
    public void Validate()
    {
        if (ProbabilityPercent is < 0 or > 100 || MaximumDelayMinutes is < 0 or > 10) throw new ArgumentException("Airline realism tuning is outside its allowed bounds.");
    }
}
public sealed record AirportGateProfile(int SchemaVersion, string Icao, int PreparationSeconds = 0, bool SimulatedReadiness = false)
{
    public static AirportGateProfile Generic { get; } = new(1, "GENERIC");
    public void Validate()
    {
        if (SchemaVersion != 1 || Icao != "GENERIC" && (Icao is not { Length: 4 } || Icao.Any(character => !char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character))) || PreparationSeconds is < 0 or > 600)
            throw new ArgumentException("Invalid airport gate profile.");
    }
}
public enum DisruptionKind { GateDocuments, GateStaff, PassengerProcessing }
public enum DisruptionStatus { Active, Resolved, Skipped, SupersededByRealHold }
public enum DisruptionEffectKind { GateReadiness, BoardingClearance, ReadyTimeEstimate }
public sealed record DisruptionEffect(Guid Id, Guid IncidentId, DisruptionEffectKind Kind, bool IsActive);
public sealed record DisruptionIncident(Guid Id, Guid SessionId, DisruptionKind Kind, DateTimeOffset StartedAt, DateTimeOffset ExpectedResolution,
    DisruptionStatus Status, ImmutableArray<DisruptionEffect> Effects);
public sealed record OperationalRealismState(RealismOptions Options, AirportGateProfile GateProfile, DateTimeOffset PreparedAt)
{
    public bool DecisionMade { get; init; }
    public bool ProjectionInitialized { get; init; }
    public bool GatePrepared { get; init; }
    public bool PreparationSkipped { get; init; }
    public DisruptionIncident? Incident { get; init; }
    public GateReadiness? GateBeforeHold { get; init; }
    public void Validate()
    {
        if (Options is null || GateProfile is null) throw new ArgumentException("Realism options and airport rules are required.");
        Options.Validate(); GateProfile.Validate();
        if (Incident is { } incident && (!DecisionMade || incident.Id == Guid.Empty || incident.SessionId == Guid.Empty || !Enum.IsDefined(incident.Kind) || !Enum.IsDefined(incident.Status) ||
            incident.ExpectedResolution <= incident.StartedAt || incident.ExpectedResolution - incident.StartedAt > TimeSpan.FromMinutes(Options.MaximumDelayMinutes) ||
            incident.Effects.IsDefault || incident.Effects.Length != 3 || incident.Effects.Any(effect => effect is null) || incident.Effects.Select(effect => effect.Id).Distinct().Count() != 3 ||
            incident.Effects.Select(effect => effect.Kind).Distinct().Count() != 3 || incident.Effects.Any(effect => effect.Id == Guid.Empty || effect.IncidentId != incident.Id || !Enum.IsDefined(effect.Kind) || effect.IsActive != (incident.Status == DisruptionStatus.Active))))
            throw new ArgumentException("Invalid linked disruption state.");
        if (GateBeforeHold is not null) GateRules.Apply(GateBeforeHold);
    }
    public static string Description(DisruptionKind kind) => kind switch
    {
        DisruptionKind.GateDocuments => "Simulated gate-document check",
        DisruptionKind.GateStaff => "Simulated gate-team delay",
        _ => "Simulated passenger-processing delay"
    };
}
public sealed record UpdateOperationalRealism(EventMetadata Metadata, OperationalRealismState Progress) : OperationEvent(Metadata);
