using System.Collections.Immutable;
using ADM.Domain;

namespace ADM.Tests.Shared;

internal sealed class OperationalScenario
{
    private int _eventNumber;
    public static DateTimeOffset Now => new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    public OperationalState State { get; private set; }
    public List<WorkflowNotice> Notices { get; } = [];
    public OperationalScenario(FlightOperationType operation = FlightOperationType.Turnaround,
        bool cancellation = true, bool manualRelease = false, ServicePolicy policy = ServicePolicy.Automatic, ServiceKind? additionalRequired = null,
        ImmutableDictionary<ServiceKind, int>? phases = null, bool concurrent = false)
    {
        var rules = Enum.GetValues<ServiceKind>().ToImmutableDictionary(kind => kind, kind =>
            new ResolvedServiceRule(kind, new ServiceRule(operation == FlightOperationType.FinalArrival && kind != ServiceKind.Deboarding ? ServicePolicy.Disabled : policy,
                kind is ServiceKind.Refuelling or ServiceKind.Boarding || kind == additionalRequired, [], "Test operational policy"), PolicyLayer.Airline, "BAW", 3));
        Guid sessionId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
        var plan = new AppliedServicePlan(Guid.Parse("a0000000-0000-0000-0000-000000000002"), "BAW", 3, new("A20N", "A320_FAMILY"), operation, rules, Now)
        { ServicePhases = phases ?? ServiceSequence.DefaultPhases };
        State = OperationalFactory.Create(sessionId, Guid.NewGuid(), plan, new(sessionId, new("sample-sarah", "Sarah", "WarmProfessional", "en-GB"), Now),
            Enum.GetValues<ServiceKind>().ToImmutableHashSet(), cancellation ? Enum.GetValues<ServiceKind>().ToImmutableHashSet() : [],
            manualRelease ? [] : Enum.GetValues<ReleaseAction>().ToImmutableHashSet()) with
        { ValidatedConcurrentGroups = concurrent ? [ImmutableHashSet.Create(ServiceKind.Water, ServiceKind.Lavatory, ServiceKind.Refuelling, ServiceKind.Catering)] : [] };
    }
    public EventMetadata Meta(EvidenceSource source = EvidenceSource.Simulation, string? evidence = "Explicit test observation")
    {
        Guid id = Guid.Parse($"00000000-0000-0000-0000-{++_eventNumber:000000000000}");
        return new(id, State.Session.Id, Now.AddSeconds(_eventNumber), source, id, evidence is null ? null : new(source, evidence, Now.AddSeconds(_eventNumber)));
    }
    public Reduction Apply(OperationEvent input)
    {
        var reduced = OperationalReducer.Apply(State, input);
        State = reduced.State;
        Notices.AddRange(reduced.Notices);
        return reduced;
    }
    public Reduction User(PilotDirective directive, ServiceKind? kind = null) => Apply(new PilotInstruction(Meta(EvidenceSource.User), directive, kind));
    public void GateReady(GateProvenance provenance = GateProvenance.Simulation)
    {
        var metadata = Meta(provenance == GateProvenance.PilotAttested ? EvidenceSource.User : provenance == GateProvenance.VerifiedExternal ? EvidenceSource.Gsx : EvidenceSource.Simulation);
        Apply(new ObserveGate(metadata, new(GateReadinessState.Ready, provenance, metadata.Evidence)));
    }
    public void Pulse() => Apply(new EvaluateOperation(Meta()));
    public void Start(ServiceKind kind)
    {
        var command = State.Commands.Last(command => command.Kind == OperationalCommandKind.RequestService && command.Service == kind);
        Apply(new AcknowledgeCommand(Meta(EvidenceSource.System, null), command.Id, CommandOutcome.Accepted));
        Apply(new ObserveService(Meta(), kind, ServiceState.Active));
    }
    public void CompletePreboarding()
    {
        Pulse();
        for (int step = 0; step < 32; step++)
        {
            var next = State.Session.Services.Values.FirstOrDefault(service => service.Kind != ServiceKind.Boarding &&
                service.State is ServiceState.RequestPending or ServiceState.Requested or ServiceState.Active);
            if (next is null) return;
            if (next.State != ServiceState.Active) Start(next.Kind);
            Apply(new ObserveService(Meta(), next.Kind, ServiceState.Completed));
        }
        throw new InvalidOperationException("The service pipeline did not settle.");
    }
    public void BoardAndComplete()
    {
        GateReady(); CompletePreboarding(); User(PilotDirective.ApproveBoarding); Start(ServiceKind.Boarding);
        Apply(new ObserveService(Meta(), ServiceKind.Boarding, ServiceState.Completed));
    }
}
