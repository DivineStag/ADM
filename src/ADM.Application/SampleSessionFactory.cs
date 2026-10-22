using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application;

/// <summary>Shared, explicitly synthetic M0 data. Never a representation of airline SOP.</summary>
public sealed class SampleSessionFactory(IOperationalClock clock, IServicePlanResolver resolver)
{
    public static AirlineProfile GenericProfile()
    {
        var operations = Enum.GetValues<FlightOperationType>().ToImmutableDictionary(operation => operation,
            operation => new OperationPolicy(Enum.GetValues<ServiceKind>().ToImmutableDictionary(kind => kind,
                kind => new ServiceRule(
                    kind is ServiceKind.Deboarding or ServiceKind.Cleaning || operation == FlightOperationType.FinalArrival
                        ? ServicePolicy.Disabled : ServicePolicy.Automatic,
                    operation != FlightOperationType.FinalArrival && kind is ServiceKind.Refuelling or ServiceKind.Boarding,
                    ImmutableHashSet<ServiceKind>.Empty, "Requires future capability and prerequisite evaluation"))));
        return new(1, "GENERIC", "Generic sample preferences", 1, operations,
            ImmutableDictionary<string, AircraftProfileOverride>.Empty,
            ImmutableDictionary<string, AircraftProfileOverride>.Empty);
    }

    public DutySession Create()
    {
        Guid dutyId = Guid.NewGuid(), sectorId = Guid.NewGuid(), sessionId = Guid.NewGuid();
        var aircraft = new AircraftIdentity("A20N", "A320_FAMILY");
        var plan = resolver.Resolve(GenericProfile(), null, aircraft, FlightOperationType.FirstDeparture,
            new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty));
        var services = plan.Services.ToImmutableDictionary(pair => pair.Key, pair => new ServiceInstance(
            Guid.NewGuid(), sessionId, pair.Key,
            pair.Value.Rule.Policy == ServicePolicy.Disabled ? ServiceState.NotRequired : ServiceState.Planned,
            null, CompletionProvenance.Unknown));
        var assignment = new DispatchAssignment(sessionId, new("sample-sarah", "Sarah", "WarmProfessional", "en-GB"), clock.UtcNow);
        var session = new GroundHandlingSession(sessionId, sectorId, FlightOperationType.FirstDeparture,
            WorkflowStage.Planning, plan, assignment, new(GateReadinessState.Unknown, GateProvenance.Unknown, null),
            services, [], [], 0);
        var sector = new SectorLeg(sectorId, dutyId, 1, new("SAMPLE", "EGLL", "EHAM", aircraft, clock.UtcNow), session);
        return new(dutyId, DutyState.GroundHandling, [sector]);
    }
}
