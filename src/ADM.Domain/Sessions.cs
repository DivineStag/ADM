using System.Collections.Immutable;

namespace ADM.Domain;

public enum FlightPlanSource { Manual, SimBrief, Gsx }
public sealed record FlightPlanSnapshot(string Callsign, string Origin, string Destination,
    AircraftIdentity Aircraft, DateTimeOffset CreatedAt)
{
    public FlightPlanSource Source { get; init; }
    public int? PlannedPassengers { get; init; }
    public double? PlannedRampFuelKg { get; init; }
    public double? PlannedZeroFuelWeightKg { get; init; }
    public string? SourceAircraftTitle { get; init; }
    public DateTimeOffset? ScheduledDepartureUtc { get; init; }
}
public sealed record GroundHandlingSession(Guid Id, Guid SectorId, FlightOperationType Operation,
    WorkflowStage Stage, AppliedServicePlan Plan, DispatchAssignment Dispatcher, GateReadiness Gate,
    ImmutableDictionary<ServiceKind, ServiceInstance> Services, ImmutableArray<PilotApproval> Approvals,
    ImmutableArray<OperationalHold> Holds, int PrerequisiteRevision);
public sealed record SectorLeg(Guid Id, Guid DutyId, int Number, FlightPlanSnapshot FlightPlan,
    GroundHandlingSession GroundHandling);
public sealed record DutySession(Guid Id, DutyState State, ImmutableArray<SectorLeg> Sectors);
public sealed record CommandJournalEntry(Guid CommandId, Guid SessionId, ServiceKind Kind, int IntentVersion,
    Guid PlanSnapshotId, string PolicySource, DateTimeOffset CreatedAt, string Outcome);
public sealed record RecoveryCheckpoint(Guid SessionId, DateTimeOffset SavedAt,
    ImmutableArray<CommandJournalEntry> Journal, ImmutableDictionary<ServiceKind, ServiceInstance> LastKnownServices,
    bool RequiresReconciliation);
public sealed record CompatibilityReport(string Tier, ImmutableArray<string> Limitations, DateTimeOffset AssessedAt);
