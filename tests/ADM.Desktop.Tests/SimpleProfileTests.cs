using System.Collections.Immutable;
using System.IO;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Desktop.Presentation;
using ADM.Domain;
using ADM.Infrastructure;

namespace ADM.Desktop.Tests;

public sealed class SimpleProfileTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private static ProfileEditorViewModel Editor() => new(new FoundationServicePlanResolver(new Clock()));
    private static PolicyEditorRow Catering(ProfileEditorViewModel editor) => editor.Rows.Single(row => row.Kind == ServiceKind.Catering);
    [Theory]
    [InlineData("BA", "Airline")]
    [InlineData("../", "Airline")]
    [InlineData("BAW", "Duplicate")]
    [InlineData("EZY", "")]
    public void New_airline_requires_a_unique_three_letter_code_and_name(string code, string name)
    {
        var editor = Editor(); editor.NewAirlineCode = code; editor.NewAirlineName = name;
        Assert.False(editor.CreateProfileCommand.CanExecute(null));
        Assert.Equal("BAW", editor.SelectedAirline.Icao);
    }
    [Fact]
    public void Fleet_and_multiple_aircraft_edits_remain_independent_across_selection_and_reload()
    {
        var editor = Editor(); editor.NewAirlineCode = "ezy"; editor.NewAirlineName = "My easyJet profile";
        editor.CreateProfileCommand.Execute(null); Assert.Equal("EZY", editor.SelectedAirline.Icao);
        Catering(editor).Mode = OverrideMode.Ask;
        editor.SelectedScope = editor.Scopes.Single(scope => scope.Key == "type");
        editor.AircraftType = "B738"; Catering(editor).Mode = OverrideMode.Disabled; Catering(editor).Phase = 4;
        editor.AircraftType = "B77W";
        Assert.Equal(OverrideMode.Inherit, Catering(editor).Mode); Assert.Equal(ServicePolicy.Ask, editor.PreviewPlan.Services[ServiceKind.Catering].Rule.Policy);
        editor.AircraftType = "B738";
        Assert.Equal(OverrideMode.Disabled, Catering(editor).Mode); Assert.Equal(4, Catering(editor).Phase);
        editor.SelectedScope = editor.Scopes.Single(scope => scope.Key == "airline");
        Assert.Equal(OverrideMode.Ask, Catering(editor).Mode); Assert.Equal(3, Catering(editor).Phase);
        var candidate = editor.EditedProfile!; ProfileJsonCodec.Validate(candidate);
        var reopened = Editor(); reopened.LoadProfiles(ImmutableDictionary<string, AirlineProfile>.Empty.Add("EZY", ProfileJsonCodec.Parse(ProfileJsonCodec.Serialize(candidate))));
        reopened.SelectedAirline = reopened.Airlines.Single(airline => airline.Icao == "EZY");
        reopened.SelectedScope = reopened.Scopes.Single(scope => scope.Key == "type"); reopened.AircraftType = "B738";
        Assert.Equal(OverrideMode.Disabled, Catering(reopened).Mode); Assert.Equal(4, Catering(reopened).Phase);
        Assert.Equal(2, reopened.EditedProfile!.Revision); Assert.Contains("B77W", reopened.AircraftChoices);
        Catering(reopened).Mode = OverrideMode.Inherit;
        Assert.Equal(ServicePolicy.Ask, reopened.PreviewPlan.Services[ServiceKind.Catering].Rule.Policy);
        Assert.Equal(ServicePolicy.Disabled, candidate.TypeOverrides["B738"].Operations[FlightOperationType.Turnaround].Services[ServiceKind.Catering].Policy);
    }
    [Fact]
    public void Copying_a_profile_copies_other_operations_without_modifying_the_original()
    {
        var editor = Editor(); Catering(editor).Mode = OverrideMode.Disabled;
        var original = editor.EditedProfile!;
        editor.CopyCurrentProfile = true; editor.NewAirlineCode = "ABC"; editor.NewAirlineName = "My airline"; editor.CreateProfileCommand.Execute(null);
        Assert.Equal(ServicePolicy.Disabled, editor.EditedProfile!.Operations[FlightOperationType.Turnaround].Services[ServiceKind.Catering].Policy);
        Catering(editor).Mode = OverrideMode.Automatic;
        Assert.Equal(ServicePolicy.Disabled, original.Operations[FlightOperationType.Turnaround].Services[ServiceKind.Catering].Policy);
        Assert.Equal(1, editor.EditedProfile.Revision);
    }
    [Fact]
    public async Task Custom_profile_is_discovered_on_startup_and_its_callsign_resolves_without_a_network_lookup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "adm-custom-airline-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var repository = new JsonAirlineProfileRepository(Path.Combine(directory, "profiles"));
            await repository.SaveAsync(SampleSessionFactory.GenericProfile() with { Icao = "EZY", DisplayName = "My easyJet profile" }, CancellationToken.None);
            var listed = await repository.ListAsync(CancellationToken.None); Assert.Single(listed); Assert.Equal("EZY", listed[0].Icao);
            var identity = new AirlineIdentityResolver(["BAW"]); Assert.Equal("GENERIC", identity.Resolve(new("EZY123", null, null, false), null).Icao);
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var clock = new Clock(); var personas = PersonaCatalogue.LoadBundled();
            using var vm = new LiveOperationsViewModel(clock, new FoundationServicePlanResolver(clock), identity, repository, store, store, new GsxOnlyFlightImporter(),
                new LocalConversationEngine(personas), personas, new JsonAircraftCapabilityCatalog(Path.Combine(directory, "aircraft.json")));
            await vm.InitializeAsync(); Assert.Contains("EZY", vm.Airlines); Assert.Contains("EZY", vm.SavedProfiles.Keys);
            Assert.Equal("EZY", identity.Resolve(new("EZY123", null, null, false), null).Icao);
            Assert.Equal("GENERIC", identity.Resolve(new("XYZ123", null, null, false), null).Icao);
        }
        finally { Directory.Delete(directory, true); }
    }
}
