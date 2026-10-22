using System.Collections.Immutable;

namespace ADM.Domain;

public enum QuestionKind { None, Boarding, Closure, Service, MandatorySkip, GateCheck, FuelCheck, ManualRelease, ServiceCompletion, ServicesStart }
public enum PilotDirective { Status, Delay, ApproveBoarding, HoldBoarding, ApproveClosure, HoldClosure, Skip, ConfirmSkip, Cancel, Resume, ApproveService, SkipSimulatedDelay, ConfirmGate, ConfirmFuel, ConfirmRelease, Help, Unknown, KeepService, ConfirmServiceCompletion, ApproveServicesStart, HoldServicesStart }
public enum ReleaseAction { Doors, JetwayOrStairs, GroundEquipment }
public enum OperationalCommandKind { RequestService, CancelService, ReleaseEquipment }
public enum CommandOutcome { Accepted, Rejected, Unknown }
public sealed record PendingQuestion(QuestionKind Kind, int Revision, ServiceKind? Service = null, ReleaseAction? Release = null);
public sealed record OperationalCommand(Guid Id, Guid SessionId, OperationalCommandKind Kind, ServiceKind? Service,
    ReleaseAction? Release, int IntentVersion, Guid PlanSnapshotId, string PolicySource, ServiceState PreviousState)
{
    public int PrerequisiteRevision { get; init; }
}
public sealed record WorkflowNotice(string Key, string? Text = null, bool Important = false);
public sealed record OperationalState(GroundHandlingSession Session, ConnectionState Connection,
    ImmutableHashSet<ServiceKind> SupportedServices, ImmutableHashSet<ServiceKind> CancellableServices,
    ImmutableHashSet<ReleaseAction> SupportedReleaseActions,
    ImmutableDictionary<ReleaseAction, EquipmentRelease> Releases,
    ImmutableHashSet<Guid> SeenEvents, ImmutableArray<OperationalCommand> Commands,
    ImmutableHashSet<Guid> AcknowledgedCommands, ImmutableHashSet<ServiceKind> ServicePermissions,
    PendingQuestion? Question, ServiceKind? FocusService, bool FuelAttested, bool ClosureHeld)
{
    public ImmutableHashSet<ServiceKind> PilotOverrides { get; init; } = [];
    public bool RequiresServiceStartApproval { get; init; }
    public bool GsxFlightPlanReceived { get; init; }
    public bool PlannedLoadFiguresReceived { get; init; }
    public bool ServicesHaveStarted { get; init; }
    public OperationalRealismState? Realism { get; init; }
    public Evidence? ServiceStartApproval { get; init; }
    // Pilot-requested dispatch pairing; actual service execution remains GSX-controlled.
    public bool RequestFuelAndCateringTogether { get; init; }
    public ImmutableHashSet<ServiceKind> ServiceCompletionChecks { get; init; } = [];
    public ImmutableHashSet<ServiceKind>? TriggerableServices { get; init; }
    // Empty unless an adapter/aircraft compatibility check has explicitly validated overlap.
    public ImmutableArray<ImmutableHashSet<ServiceKind>> ValidatedConcurrentGroups { get; init; } = [];
    public Evidence? FuelAttestation { get; init; }
    public ImmutableDictionary<ServiceKind, Evidence> ServicePermissionEvidence { get; init; } = ImmutableDictionary<ServiceKind, Evidence>.Empty;
}
public sealed record Reduction(OperationalState State, ImmutableArray<WorkflowNotice> Notices,
    ImmutableArray<OperationalCommand> NewCommands);

public abstract record OperationEvent(EventMetadata Metadata);
public sealed record EvaluateOperation(EventMetadata Metadata) : OperationEvent(Metadata);
public sealed record ObserveService(EventMetadata Metadata, ServiceKind Kind, ServiceState State) : OperationEvent(Metadata);
public sealed record RequestServiceCompletionCheck(EventMetadata Metadata, ServiceKind Kind) : OperationEvent(Metadata);
public sealed record ObserveServiceCycleEnd(EventMetadata Metadata, ServiceKind Kind) : OperationEvent(Metadata);
public sealed record AcknowledgeCommand(EventMetadata Metadata, Guid CommandId, CommandOutcome Outcome) : OperationEvent(Metadata);
public sealed record ObserveGate(EventMetadata Metadata, GateReadiness Gate) : OperationEvent(Metadata);
public sealed record ChangeHold(EventMetadata Metadata, OperationalHold Hold, bool Notify = true) : OperationEvent(Metadata);
public sealed record ChangeConnection(EventMetadata Metadata, ConnectionState Connection) : OperationEvent(Metadata);
public sealed record PilotInstruction(EventMetadata Metadata, PilotDirective Directive, ServiceKind? Service = null,
    ReleaseAction? Release = null) : OperationEvent(Metadata);
public sealed record ObserveRelease(EventMetadata Metadata, ReleaseAction Action) : OperationEvent(Metadata);
// An explicit reconciliation decision, never a normal progress event or an automatic restart replay.
public sealed record ReconcileService(EventMetadata Metadata, ServiceKind Kind, ServiceState State) : OperationEvent(Metadata);
public sealed record UpdateCapabilities(EventMetadata Metadata, ImmutableHashSet<ServiceKind> Services,
    ImmutableHashSet<ServiceKind> Cancellations, ImmutableArray<ImmutableHashSet<ServiceKind>> ConcurrentGroups) : OperationEvent(Metadata);
public sealed record UpdateServiceAvailability(EventMetadata Metadata, ImmutableHashSet<ServiceKind> Triggerable) : OperationEvent(Metadata);

public static class OperationalFactory
{
    public static OperationalState Create(Guid sessionId, Guid sectorId, AppliedServicePlan plan,
        DispatchAssignment dispatcher, ImmutableHashSet<ServiceKind> supported,
        ImmutableHashSet<ServiceKind> cancellable, ImmutableHashSet<ReleaseAction> releaseActions)
    {
        if (sessionId == Guid.Empty || sectorId == Guid.Empty || dispatcher.SessionId != sessionId || plan.SnapshotId == Guid.Empty || plan.ProfileRevision < 1)
            throw new ArgumentException("Session, sector and dispatcher identities must match.");
        ServicePlanRules.Validate(plan);
        var services = plan.Services.ToImmutableDictionary(pair => pair.Key, pair => new ServiceInstance(
            StableId(sessionId, (int)pair.Key + 1), sessionId, pair.Key,
            !ServicePlanRules.Applicable(pair.Key, plan.Operation, pair.Value.Rule) || pair.Value.Rule.Policy == ServicePolicy.Disabled
                ? ServiceState.NotRequired : ServiceState.Planned,
            new(EvidenceSource.System, $"Policy {pair.Value.ProfileKey}/{pair.Value.ProfileRevision}/{plan.SnapshotId}", plan.AppliedAt), CompletionProvenance.Unknown));
        var session = new GroundHandlingSession(sessionId, sectorId, plan.Operation, WorkflowStage.Planning, plan, dispatcher,
            new(GateReadinessState.Unknown, GateProvenance.Unknown, null), services, [], [], 0);
        return new(session, ConnectionState.Connected, supported, cancellable, releaseActions,
            Enum.GetValues<ReleaseAction>().ToImmutableDictionary(action => action,
                action => new EquipmentRelease(action.ToString(), false, null, CompletionProvenance.Unknown)),
            [], [], [], [], null, null, false, false);
    }

    internal static Guid StableId(Guid seed, int discriminator)
    {
        Span<byte> bytes = stackalloc byte[20];
        seed.TryWriteBytes(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], discriminator);
        return new Guid(System.Security.Cryptography.SHA256.HashData(bytes).AsSpan(0, 16));
    }
}

public static class ServicePlanRules
{
    public static bool Applicable(ServiceKind kind, FlightOperationType operation, ServiceRule? rule = null) =>
        !(rule?.Applicability == ServiceApplicability.DepartureOnly && operation == FlightOperationType.FinalArrival) &&
        !(rule?.Applicability == ServiceApplicability.ArrivalOnly && operation == FlightOperationType.FirstDeparture) &&
        !(operation == FlightOperationType.FirstDeparture && kind == ServiceKind.Deboarding) &&
        !(operation == FlightOperationType.FinalArrival && kind is ServiceKind.Refuelling or ServiceKind.Boarding);

    public static ImmutableHashSet<ServiceKind> Dependencies(AppliedServicePlan plan, ServiceKind kind)
    {
        var dependencies = plan.Services[kind].Rule.Dependencies;
        if (kind == ServiceKind.Cleaning) dependencies = dependencies.Add(ServiceKind.Deboarding);
        if (kind is ServiceKind.Catering or ServiceKind.Refuelling or ServiceKind.Water or ServiceKind.Lavatory)
            dependencies = dependencies.Add(ServiceKind.Cleaning).Add(ServiceKind.Deboarding);
        if (kind == ServiceKind.Boarding) dependencies = dependencies.Union(Enum.GetValues<ServiceKind>().Where(service => service != ServiceKind.Boarding));
        dependencies = dependencies.Union(plan.ServicePhases.Where(pair => pair.Value < plan.ServicePhases[kind]).Select(pair => pair.Key));
        return dependencies;
    }

    public static void Validate(AppliedServicePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ServiceSequence.Validate(plan.ServicePhases);
        if (!Enum.IsDefined(plan.Operation) || plan.Services is null || plan.Services.Count != 7 || Enum.GetValues<ServiceKind>().Any(kind => !plan.Services.ContainsKey(kind)))
            throw new ArgumentException("A resolved plan must contain all seven services and a known operation.");
        foreach (var pair in plan.Services)
        {
            if (pair.Key != pair.Value.Kind || !Enum.IsDefined(pair.Value.Rule.Policy) || !Enum.IsDefined(pair.Value.Rule.Applicability) || pair.Value.Rule.Dependencies is null ||
                pair.Value.Rule.Dependencies.Any(dependency => !Enum.IsDefined(dependency)))
                throw new ArgumentException("Invalid service rule.");
            if (pair.Value.Rule.Required && pair.Value.Rule.Policy == ServicePolicy.Disabled && Applicable(pair.Key, plan.Operation, pair.Value.Rule))
                throw new ArgumentException("An applicable required service cannot be disabled. Change its applicability or required status explicitly.");
        }
        var visited = new HashSet<ServiceKind>();
        var visiting = new HashSet<ServiceKind>();
        foreach (var kind in Enum.GetValues<ServiceKind>()) Visit(kind);
        void Visit(ServiceKind kind)
        {
            if (visited.Contains(kind)) return;
            if (!visiting.Add(kind)) throw new ArgumentException("Service dependencies contain a cycle.");
            foreach (var dependency in Dependencies(plan, kind)) Visit(dependency);
            visiting.Remove(kind);
            visited.Add(kind);
        }
    }
}
