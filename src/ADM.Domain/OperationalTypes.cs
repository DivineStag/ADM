namespace ADM.Domain;

public enum ServiceKind { Deboarding, Cleaning, Catering, Refuelling, Water, Lavatory, Boarding }
public enum ServiceState { NotRequired, Planned, Eligible, RequestPending, Requested, Active, Completed, Skipped, CancelPending, Cancelled, Failed, Unknown, CycleEnded }
public enum WorkflowStage { Uninitialized, Planning, ArrivalHandling, PreBoardServices, AwaitingBoardingApproval, Boarding, AwaitingClosureApproval, ClosingAndReleasing, Completed, Suspended, NeedsAttention }
public enum FlightOperationType { FirstDeparture, Turnaround, FinalArrival }
public enum ServicePolicy { Automatic, Ask, Disabled }
public enum HoldKind { SimulatedGate, SimulatedPassenger, SimulatedStaff, RealServiceBlocked, ConnectionIssue, ManualPilotHold }
public enum HoldScope { AllOperations, Boarding }
public enum GateReadinessState { Unknown, NotApplicable, Preparing, Held, Ready, Invalidated }
public enum EvidenceSource { Gsx, SimConnect, User, Simulation, System }
public enum GateProvenance { Unknown, Simulation, PilotAttested, VerifiedExternal }
public enum DutyState { Idle, GroundHandling, ReleasedForFlight, EnrouteObserved, ArrivalCandidate, AwaitingNextPlan, Paused, Ended }
public enum ApprovalKind { Boarding, Closure }
public enum CompletionProvenance { Unknown, VerifiedExternal, PilotAttested, Simulation }
public enum ConnectionState { Offline, Connected, Reconnecting, ReconciliationRequired }

public sealed record Evidence(EvidenceSource Source, string Reference, DateTimeOffset ObservedAt);
public sealed record EventMetadata(Guid EventId, Guid SessionId, DateTimeOffset OccurredAt,
    EvidenceSource Source, Guid CorrelationId, Evidence? Evidence);
public sealed record DomainEvent(EventMetadata Metadata, string Name, string? Detail = null);
public sealed record GateReadiness(GateReadinessState State, GateProvenance Provenance, Evidence? Evidence);
public sealed record OperationalHold(Guid Id, HoldKind Kind, string Reason, bool IsActive,
    DateTimeOffset? ExpectedResolution, Guid? IncidentId, HoldScope Scope = HoldScope.AllOperations)
{
    public bool IsSimulated => Kind is HoldKind.SimulatedGate or HoldKind.SimulatedPassenger or HoldKind.SimulatedStaff;
}
public sealed record PilotApproval(Guid Id, Guid SessionId, ApprovalKind Kind, int PrerequisiteRevision,
    DateTimeOffset ApprovedAt, bool IsInvalidated);
public sealed record ServiceInstance(Guid Id, Guid SessionId, ServiceKind Kind, ServiceState State,
    Evidence? LastEvidence, CompletionProvenance CompletionProvenance);
public sealed record AircraftIdentity(string? IcaoType, string? Family);
public sealed record DispatcherPersona(string Id, string Name, string Tone, string Language);
public sealed record DispatchAssignment(Guid SessionId, DispatcherPersona Persona, DateTimeOffset AssignedAt);
public sealed record ConversationMessage(Guid Id, Guid SessionId, string SemanticKey, string Sender,
    string Text, DateTimeOffset SentAt)
{
    public bool IsSystem { get; init; }
}
public sealed record EquipmentRelease(string Equipment, bool IsSatisfied, Evidence? Evidence,
    CompletionProvenance Provenance);
