using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application.Tests;

public sealed class ServicePhaseProfileTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void Phase_overrides_follow_airline_family_type_precedence_and_capture_immutable_snapshots()
    {
        var baseline = SampleSessionFactory.GenericProfile();
        ImmutableDictionary<FlightOperationType, OperationPolicy> Layer(int phase) => ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty.Add(
            FlightOperationType.Turnaround, new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty) { ServicePhases = ServiceSequence.DefaultPhases.SetItem(ServiceKind.Catering, phase) });
        var airline = baseline with
        {
            Icao = "BAW",
            Operations = Layer(4),
            FamilyOverrides = ImmutableDictionary<string, AircraftProfileOverride>.Empty.Add("A320_FAMILY", new("A320_FAMILY", Layer(5))),
            TypeOverrides = ImmutableDictionary<string, AircraftProfileOverride>.Empty.Add("A20N", new("A20N", Layer(6)))
        };
        var resolver = new FoundationServicePlanResolver(new Clock());
        var empty = new PilotServiceOverrides(ImmutableDictionary<ServiceKind, ServiceRule>.Empty);
        Assert.Equal(4, resolver.Resolve(baseline, airline, new("B77W", null), FlightOperationType.Turnaround, empty).ServicePhases[ServiceKind.Catering]);
        Assert.Equal(5, resolver.Resolve(baseline, airline, new(null, "A320_FAMILY"), FlightOperationType.Turnaround, empty).ServicePhases[ServiceKind.Catering]);
        var plan = resolver.Resolve(baseline, airline, new("A20N", "A320_FAMILY"), FlightOperationType.Turnaround, empty);
        Assert.Equal(6, plan.ServicePhases[ServiceKind.Catering]);
        airline = airline with { TypeOverrides = ImmutableDictionary<string, AircraftProfileOverride>.Empty };
        Assert.Equal(5, resolver.Resolve(baseline, airline, new("A20N", "A320_FAMILY"), FlightOperationType.Turnaround, empty).ServicePhases[ServiceKind.Catering]);
        Assert.Equal(6, plan.ServicePhases[ServiceKind.Catering]);
    }
    [Fact]
    public void Profile_json_roundtrip_preserves_service_phases_and_old_profiles_inherit_defaults()
    {
        var profile = SampleSessionFactory.GenericProfile();
        var policy = profile.Operations[FlightOperationType.Turnaround] with { ServicePhases = ServiceSequence.DefaultPhases.SetItem(ServiceKind.Lavatory, 4) };
        profile = profile with { Operations = profile.Operations.SetItem(FlightOperationType.Turnaround, policy) };
        string json = JsonSerializer.Serialize(profile, Options);
        Assert.Equal(4, ProfileJsonCodec.Parse(json).Operations[FlightOperationType.Turnaround].ServicePhases![ServiceKind.Lavatory]);
        var previous = ProfileJsonCodec.Parse(JsonSerializer.Serialize(SampleSessionFactory.GenericProfile(), Options));
        Assert.Null(previous.Operations[FlightOperationType.Turnaround].ServicePhases);
        var plan = new FoundationServicePlanResolver(new Clock()).Resolve(previous, null, new(null, null), FlightOperationType.Turnaround,
            new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty));
        Assert.Equal(ServiceSequence.DefaultPhases, plan.ServicePhases);
    }
    [Fact]
    public void Invalid_fuel_order_in_json_preserves_last_valid_profile()
    {
        var valid = SampleSessionFactory.GenericProfile();
        var invalid = valid with
        {
            Operations = valid.Operations.SetItem(FlightOperationType.Turnaround,
            valid.Operations[FlightOperationType.Turnaround] with { ServicePhases = ServiceSequence.DefaultPhases.SetItem(ServiceKind.Refuelling, 7) })
        };
        var loaded = ProfileJsonCodec.Load(JsonSerializer.Serialize(invalid, Options), valid, valid);
        Assert.True(loaded.UsedFallback); Assert.Same(valid, loaded.Profile); Assert.NotNull(loaded.Diagnostic);
    }
}
