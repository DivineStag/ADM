using System.Collections.Immutable;
using ADM.Application;
using ADM.Contracts;
using ADM.Desktop.Presentation;
using ADM.Domain;

namespace ADM.Desktop.Tests;

public sealed class SavedProfileEditorTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    [Fact]
    public void Editing_policy_preserves_dependencies_applicability_other_operations_and_type_phase_override()
    {
        var profile = SampleSessionFactory.GenericProfile() with { Icao = "BAW", Revision = 3 };
        var operation = profile.Operations[FlightOperationType.Turnaround];
        var rule = operation.Services[ServiceKind.Catering] with { Dependencies = [ServiceKind.Refuelling], Applicability = ServiceApplicability.DepartureOnly };
        var type = new AircraftProfileOverride("A20N", ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty.Add(FlightOperationType.Turnaround,
            new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty.Add(ServiceKind.Catering, rule)) { ServicePhases = ServiceSequence.DefaultPhases.SetItem(ServiceKind.Catering, 4) }));
        profile = profile with
        {
            Operations = profile.Operations.SetItem(FlightOperationType.Turnaround, operation with { Services = operation.Services.SetItem(ServiceKind.Catering, rule) }),
            TypeOverrides = profile.TypeOverrides.Add("A20N", type)
        };
        var editor = new ProfileEditorViewModel(new FoundationServicePlanResolver(new Clock()));
        editor.LoadProfiles(ImmutableDictionary<string, AirlineProfile>.Empty.Add("BAW", profile));
        editor.Rows.Single(row => row.Kind == ServiceKind.Catering).Airline = ServicePolicy.Ask;
        var candidate = editor.EditedProfile!;
        Assert.Empty(editor.SequenceError); Assert.Equal(4, candidate.Revision);
        Assert.Contains(ServiceKind.Refuelling, candidate.Operations[FlightOperationType.Turnaround].Services[ServiceKind.Catering].Dependencies);
        Assert.Equal(ServiceApplicability.DepartureOnly, candidate.Operations[FlightOperationType.Turnaround].Services[ServiceKind.Catering].Applicability);
        Assert.Equal(4, candidate.TypeOverrides["A20N"].Operations[FlightOperationType.Turnaround].ServicePhases![ServiceKind.Catering]);
        Assert.Equal(profile.Operations[FlightOperationType.FinalArrival], candidate.Operations[FlightOperationType.FinalArrival]);
        Assert.Equal(3, profile.Revision);
    }
    [Fact]
    public void Saved_profiles_reload_on_airline_operation_and_aircraft_type_selection()
    {
        var profile = SampleSessionFactory.GenericProfile() with { Icao = "BAW" };
        var rules = ImmutableDictionary<ServiceKind, ServiceRule>.Empty.Add(ServiceKind.Catering, new(ServicePolicy.Disabled, false, [], "Fixture"));
        profile = profile with { TypeOverrides = profile.TypeOverrides.Add("B77W", new("B77W", ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty.Add(FlightOperationType.Turnaround, new(rules)))) };
        var editor = new ProfileEditorViewModel(new FoundationServicePlanResolver(new Clock()));
        editor.LoadProfiles(ImmutableDictionary<string, AirlineProfile>.Empty.Add("BAW", profile));
        editor.AircraftType = "B77W";
        Assert.Equal(OverrideMode.Disabled, editor.Rows.Single(row => row.Kind == ServiceKind.Catering).Type);
        editor.AircraftType = "A20N";
        Assert.Equal(OverrideMode.Inherit, editor.Rows.Single(row => row.Kind == ServiceKind.Catering).Type);
        editor.AircraftType = "B77W";
        Assert.Equal(ServicePolicy.Disabled, editor.PreviewPlan.Services[ServiceKind.Catering].Rule.Policy);
    }
}
