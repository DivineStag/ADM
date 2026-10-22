using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application.Tests;

public sealed class ProfileSnapshotTests
{
    private sealed class FixedClock : IOperationalClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    }
    private readonly FoundationServicePlanResolver _resolver = new(new FixedClock());
    private static ServiceRule Rule(ServicePolicy policy) => new(policy, false, [], "fixture");
    private static ImmutableDictionary<FlightOperationType, OperationPolicy> Operations(ServicePolicy policy) =>
        ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty.Add(FlightOperationType.Turnaround,
            new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty.Add(ServiceKind.Catering, Rule(policy))));

    [Fact]
    public void Precedence_is_generic_airline_family_exact_type_then_current_flight()
    {
        var generic = SampleSessionFactory.GenericProfile();
        var airline = generic with
        {
            Icao = "BAW",
            Revision = 4,
            Operations = Operations(ServicePolicy.Disabled),
            FamilyOverrides = ImmutableDictionary<string, AircraftProfileOverride>.Empty.Add("A320_FAMILY",
                new("A320_FAMILY", Operations(ServicePolicy.Automatic))),
            TypeOverrides = ImmutableDictionary<string, AircraftProfileOverride>.Empty.Add("A20N",
                new("A20N", Operations(ServicePolicy.Ask)))
        };
        var empty = new PilotServiceOverrides(ImmutableDictionary<ServiceKind, ServiceRule>.Empty);
        AppliedServicePlan Resolve(AircraftIdentity aircraft, PilotServiceOverrides overrides) =>
            _resolver.Resolve(generic, airline, aircraft, FlightOperationType.Turnaround, overrides);
        Assert.Equal(PolicyLayer.Airline, Resolve(new("B77W", null), empty).Services[ServiceKind.Catering].Source);
        Assert.Equal(PolicyLayer.AircraftFamily, Resolve(new(null, "A320_FAMILY"), empty).Services[ServiceKind.Catering].Source);
        var snapshot = Resolve(new("A20N", "A320_FAMILY"), empty);
        Assert.Equal(PolicyLayer.AircraftType, snapshot.Services[ServiceKind.Catering].Source);
        Assert.Equal(ServicePolicy.Ask, snapshot.Services[ServiceKind.Catering].Rule.Policy);
        var current = Resolve(new("A20N", "A320_FAMILY"), new(empty.Services.Add(ServiceKind.Catering, Rule(ServicePolicy.Disabled))));
        Assert.Equal(PolicyLayer.CurrentFlight, current.Services[ServiceKind.Catering].Source);
        Assert.Equal(ServicePolicy.Disabled, current.Services[ServiceKind.Catering].Rule.Policy);
        airline = airline with { Revision = 5, Operations = Operations(ServicePolicy.Automatic) };
        Assert.Equal(4, snapshot.ProfileRevision);
        Assert.Equal(ServicePolicy.Ask, snapshot.Services[ServiceKind.Catering].Rule.Policy);
        Assert.NotEqual(snapshot.SnapshotId, current.SnapshotId);
    }

    [Fact]
    public void Final_arrival_disabled_boarding_is_not_falsely_completed()
    {
        var plan = _resolver.Resolve(SampleSessionFactory.GenericProfile(), null, new(null, null),
            FlightOperationType.FinalArrival, new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty));
        Assert.Equal(ServicePolicy.Disabled, plan.Services[ServiceKind.Boarding].Rule.Policy);
        Assert.Equal(7, plan.Services.Count);
    }

    [Fact]
    public void Unsupported_schema_and_incomplete_baseline_are_rejected()
    {
        var profile = SampleSessionFactory.GenericProfile();
        var overrides = new PilotServiceOverrides(ImmutableDictionary<ServiceKind, ServiceRule>.Empty);
        Assert.Throws<ArgumentException>(() => _resolver.Resolve(profile with { SchemaVersion = 99 }, null,
            new(null, null), FlightOperationType.Turnaround, overrides));
        Assert.Throws<ArgumentException>(() => _resolver.Resolve(profile with { Operations = Operations(ServicePolicy.Ask) }, null,
            new(null, null), FlightOperationType.Turnaround, overrides));
    }
}
