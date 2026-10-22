using System.Collections.Immutable;

namespace ADM.Domain;

public enum AirlineSource { Generic, ManualSelection, SimulatorCallsign, SimBriefAtcCallsign, FlightOverride }
public enum PolicyLayer { GenericDefaults, Airline, AircraftFamily, AircraftType, CurrentFlight }
public enum ServiceApplicability { AllOperations, DepartureOnly, ArrivalOnly }
public sealed record FlightIdentity(string? SimBriefAtcCallsign, string? SimulatorAtcCallsign,
    string? ManualAirline, bool SimulatorCallsignVerified);
public sealed record UserAirlineOverride(string Icao);
public sealed record AirlineResolution(string Icao, AirlineSource Source, string? ParsedDesignator,
    bool IsValidated, bool HasSourceConflict, string Provenance);
public sealed record ServiceRule(ServicePolicy Policy, bool Required,
    ImmutableHashSet<ServiceKind> Dependencies, string ApplicabilityRule,
    ServiceApplicability Applicability = ServiceApplicability.AllOperations);
public sealed record OperationPolicy(ImmutableDictionary<ServiceKind, ServiceRule> Services)
{
    // Omission inherits the previous layer. Equal phases allow overlap only when capabilities permit it.
    public ImmutableDictionary<ServiceKind, int>? ServicePhases { get; init; }
}
public sealed record AircraftProfileOverride(string CanonicalIdentity,
    ImmutableDictionary<FlightOperationType, OperationPolicy> Operations);
public sealed record AirlineProfile(int SchemaVersion, string Icao, string DisplayName, int Revision,
    ImmutableDictionary<FlightOperationType, OperationPolicy> Operations,
    ImmutableDictionary<string, AircraftProfileOverride> FamilyOverrides,
    ImmutableDictionary<string, AircraftProfileOverride> TypeOverrides)
{
    public RealismTuning? Realism { get; init; }
}
public sealed record PilotServiceOverrides(ImmutableDictionary<ServiceKind, ServiceRule> Services);
public sealed record ResolvedServiceRule(ServiceKind Kind, ServiceRule Rule, PolicyLayer Source,
    string ProfileKey, int ProfileRevision);
// A value snapshot is captured before dispatch. Later profile edits create a new snapshot.
public sealed record AppliedServicePlan(Guid SnapshotId, string AirlineIcao, int ProfileRevision,
    AircraftIdentity Aircraft, FlightOperationType Operation,
    ImmutableDictionary<ServiceKind, ResolvedServiceRule> Services, DateTimeOffset AppliedAt)
{
    public ImmutableDictionary<ServiceKind, int> ServicePhases { get; init; } = ServiceSequence.DefaultPhases;
}
