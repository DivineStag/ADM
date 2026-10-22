using System.Collections.Immutable;
using System.IO;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Desktop.Presentation;
using ADM.Domain;
using ADM.Infrastructure;

namespace ADM.Desktop.Tests;

public sealed class M7WorkspaceTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Importer : IFlightPlanImporter
    {
        public int Calls { get; private set; }
        public Task<FlightPlanResult> ImportLatestAsync(string pilotId, CancellationToken ct) { Calls++; return Task.FromResult(new FlightPlanResult(null, "No import requested in this fixture")); }
    }
    [Fact]
    public async Task Entering_a_next_plan_stages_it_without_replacing_active_flight_or_approvals()
    {
        string path = Path.Combine(Path.GetTempPath(), "adm-m7-stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(path, "dispatch.db")); var clock = new Clock(); var resolver = new FoundationServicePlanResolver(clock); var personas = PersonaCatalogue.LoadBundled();
            var duty = new SampleSessionFactory(clock, resolver).Create(); var sector = duty.Sectors[0];
            var state = OperationalFactory.Create(sector.GroundHandling.Id, sector.Id, sector.GroundHandling.Plan, sector.GroundHandling.Dispatcher,
                Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], []);
            var local = new LocalDispatchSession(state, new LocalConversationEngine(personas), clock);
            await store.SaveAsync(new(local.Checkpoint, sector.FlightPlan, "Fixture aircraft"), CancellationToken.None);
            using var vm = new LiveOperationsViewModel(clock, resolver, new AirlineIdentityResolver(["BAW", "RYR", "UAL"]), new JsonAirlineProfileRepository(Path.Combine(path, "profiles")),
                store, store, new Importer(), new LocalConversationEngine(personas), personas, new JsonAircraftCapabilityCatalog(Path.Combine(path, "aircraft.json")));
            await vm.InitializeAsync(restorePreviousSession: true); string currentFlight = vm.FlightLabel;
            vm.Callsign = "RYR123"; vm.Origin = "EHAM"; vm.Destination = "EGLL"; vm.AircraftType = "A20N";
            vm.ManualCommand.Execute(null);
            for (int i = 0; i < 100 && !vm.ManualCommand.CanExecute(null); i++) await Task.Delay(10);
            Assert.Equal(currentFlight, vm.FlightLabel); Assert.Contains("RYR123", vm.NextFlightLabel); Assert.False(vm.NextSectorConfirmed);
            var saved = await store.LoadActiveAsync(CancellationToken.None); Assert.Equal(sector.FlightPlan, saved!.Flight);
            Assert.Equal(state.Session.Id, saved.Checkpoint.State.Session.Id); Assert.Equal(state.Session.Plan.SnapshotId, saved.Checkpoint.State.Session.Plan.SnapshotId);
            Assert.False(vm.PrepareNextCommand.CanExecute(null));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact]
    public async Task Startup_keeps_cached_flight_opt_in_and_deboarding_is_absent_from_all_profile_options()
    {
        string path = Path.Combine(Path.GetTempPath(), "adm-m7-workspace-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(path, "dispatch.db")); var clock = new Clock();
            await ((IFlightPlanCache)store).SaveAsync(new("BAW123", "EGLL", "EHAM", new("A20N", null), clock.UtcNow), CancellationToken.None);
            var personas = PersonaCatalogue.LoadBundled(); var resolver = new FoundationServicePlanResolver(clock); var importer = new Importer();
            using var vm = new LiveOperationsViewModel(clock, resolver, new AirlineIdentityResolver(["BAW", "RYR", "UAL"]), new JsonAirlineProfileRepository(Path.Combine(path, "profiles")),
                store, store, importer, new LocalConversationEngine(personas), personas, new JsonAircraftCapabilityCatalog(Path.Combine(path, "aircraft.json")));
            await vm.InitializeAsync(); Assert.Equal("No flight loaded", vm.FlightLabel); Assert.Empty(vm.Callsign); Assert.Empty(vm.Messages); Assert.False(vm.HasOperation); Assert.Equal(0, importer.Calls);
            var editor = new ProfileEditorViewModel(resolver); editor.LoadProfiles(ImmutableDictionary<string, AirlineProfile>.Empty);
            foreach (var operation in editor.Operations)
            {
                editor.SelectedOperation = operation; Assert.Equal(6, editor.Rows.Count); Assert.DoesNotContain(editor.Rows, row => row.Kind == ServiceKind.Deboarding);
                Assert.DoesNotContain(editor.Preview, row => row.Service.Contains("Deboarding", StringComparison.Ordinal));
                Assert.Equal(ServicePolicy.Disabled, editor.PreviewPlan.Services[ServiceKind.Deboarding].Rule.Policy);
            }
            Assert.False(vm.PrepareNextCommand.CanExecute(null)); Assert.False(vm.ClearChatCommand.CanExecute(null));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
