using System.Collections.Immutable;
using System.IO;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Desktop.Presentation;
using ADM.Desktop.Services;
using ADM.Domain;
using ADM.Infrastructure;

namespace ADM.Desktop.Tests;

public sealed class SimpleStartupTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    [Fact]
    public async Task GSX_flight_resolves_a_bundled_airline_and_focuses_its_editor_once_without_erasing_a_pilots_draft()
    {
        string directory = Path.Combine(Path.GetTempPath(), "adm-auto-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var clock = new Clock(); var resolver = new FoundationServicePlanResolver(clock); var personas = PersonaCatalogue.LoadBundled();
            using var live = new LiveOperationsViewModel(clock, resolver, new AirlineIdentityResolver(["BAW", "RYR", "UAL"]),
                new JsonAirlineProfileRepository(Path.Combine(directory, "profiles"), bundledDirectory: Path.Combine(AppContext.BaseDirectory, "Defaults", "Profiles")),
                store, store, new GsxOnlyFlightImporter(), new LocalConversationEngine(personas), personas, new JsonAircraftCapabilityCatalog(Path.Combine(directory, "aircraft.json")));
            var shell = new MainViewModel(clock, resolver, new SilentSound(), new TransientUiPreferencesStore(), new LocalConversationEngine(personas), personas, live);
            await live.InitializeAsync(); shell.Profiles.LoadProfiles(live.SavedProfiles);
            var flight = new FlightPlanSnapshot("EZY456", "EGPH", "EGAA", new("A319", null), clock.UtcNow) { Source = FlightPlanSource.Gsx, SourceAircraftTitle = "FenixA319 CFM WF HD" };
            live.ReceiveGsxPlan(flight);
            Assert.StartsWith("EZY", live.ProfileLabel); Assert.Equal("EZY", shell.Profiles.SelectedAirline.Icao); Assert.Equal("A319", shell.Profiles.AircraftType);
            shell.Profiles.Rows.Single(row => row.Kind == ServiceKind.Catering).Mode = OverrideMode.Disabled;
            live.ReceiveGsxPlan(flight with { PlannedPassengers = 127 });
            Assert.Equal(OverrideMode.Disabled, shell.Profiles.Rows.Single(row => row.Kind == ServiceKind.Catering).Mode);
            Assert.False(live.HasOperation); Assert.Empty(live.Messages);
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class SilentSound : INotificationPlayer
    {
        public event EventHandler<string>? PlaybackFailed { add { } remove { } }
        public void Play(double volume) { }
    }
    [Fact]
    public async Task Reopening_archives_unfinished_chat_and_states_but_preserves_request_audit_and_refreshes_GSX_load_figures()
    {
        string directory = Path.Combine(Path.GetTempPath(), "adm-fresh-launch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var clock = new Clock(); var personas = PersonaCatalogue.LoadBundled();
            var resolver = new FoundationServicePlanResolver(clock); var sector = new SampleSessionFactory(clock, resolver).Create().Sectors[0];
            var state = OperationalFactory.Create(sector.GroundHandling.Id, sector.Id, sector.GroundHandling.Plan, sector.GroundHandling.Dispatcher,
                Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], []);
            var local = new LocalDispatchSession(state, new LocalConversationEngine(personas), clock);
            local.Accept(new EvaluateOperation(local.Metadata(EvidenceSource.System)));
            var command = local.Checkpoint.State.Commands.First();
            var saved = new SavedDispatch(local.Checkpoint, sector.FlightPlan, "Fixture aircraft");
            Assert.True(await store.TryBeginSendAsync(saved, command, CancellationToken.None));
            using var vm = new LiveOperationsViewModel(clock, resolver, new AirlineIdentityResolver(["BAW"]), new JsonAirlineProfileRepository(Path.Combine(directory, "profiles")),
                store, store, new GsxOnlyFlightImporter(), new LocalConversationEngine(personas), personas, new JsonAircraftCapabilityCatalog(Path.Combine(directory, "aircraft.json")));
            await vm.InitializeAsync();
            Assert.False(vm.HasOperation); Assert.Empty(vm.Messages); Assert.Empty(vm.ServiceStatus); Assert.Empty(vm.QuickReplies); Assert.Equal("No flight loaded", vm.FlightLabel);
            Assert.Null(await store.LoadActiveAsync(CancellationToken.None));
            Assert.Contains(command.Id, await store.AttemptedAsync(saved.Checkpoint.State.Session.Id, CancellationToken.None));
            var duty = await ((IDutyStore)store).LoadDutyAsync(CancellationToken.None); Assert.Equal(DutyState.Ended, duty!.State);
            var archive = Assert.Single(await ((IDutyStore)store).HistoryAsync(duty.Id, CancellationToken.None));
            Assert.False(archive.Checkpoint.State.Session.Stage == WorkflowStage.Completed); Assert.NotEmpty(archive.Checkpoint.Messages);
            var incoming = sector.FlightPlan with { Source = FlightPlanSource.Gsx, SourceAircraftTitle = "Fixture aircraft", PlannedPassengers = null, ScheduledDepartureUtc = clock.UtcNow.AddHours(1) };
            vm.ReceiveGsxPlan(incoming); Assert.Contains(incoming.Callsign, vm.FlightLabel);
            vm.ReceiveGsxPlan(incoming with { PlannedPassengers = 180, PlannedRampFuelKg = 4200 });
            Assert.Contains("180", vm.LoadLabel); Assert.Contains("4", vm.LoadLabel); Assert.Empty(vm.Messages); Assert.False(vm.HasOperation);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void Aircraft_setup_and_startup_choice_survive_reopen_without_saving_an_account()
    {
        string directory = Path.Combine(Path.GetTempPath(), "adm-simple-setup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "setup.json"); var store = new JsonAssistantSetupStore(path);
            Assert.True(store.Load().AutoConnect); Assert.Empty(store.Load().Aircraft);
            store.Save(new(false, ImmutableHashSet.Create("A20N:Fixture_Handler", "title:Fixture aircraft")));
            var reopened = new JsonAssistantSetupStore(path).Load(); Assert.False(reopened.AutoConnect); Assert.Equal(2, reopened.Aircraft.Count);
            Assert.Contains("A20N:Fixture_Handler", reopened.Aircraft); Assert.DoesNotContain("B738:Other_Handler", reopened.Aircraft);
            Assert.DoesNotContain("pilotId", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase); Assert.False(File.Exists(path + ".tmp"));
            File.WriteAllText(path, "{malformed"); Assert.Empty(store.Load().Aircraft); Assert.True(store.Load().AutoConnect);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Normal_startup_selects_live_workspace_without_demo_or_account_fetch_and_respects_auto_connect_off()
    {
        string directory = Path.Combine(Path.GetTempPath(), "adm-simple-start-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var clock = new Clock(); var personas = PersonaCatalogue.LoadBundled();
            var preferences = new MemoryAssistantSetupStore(); preferences.Save(AssistantSetup.Default with { AutoConnect = false });
            using var vm = new LiveOperationsViewModel(clock, new FoundationServicePlanResolver(clock), new AirlineIdentityResolver(["BAW", "RYR", "UAL"]),
                new JsonAirlineProfileRepository(Path.Combine(directory, "profiles")), store, store, new GsxOnlyFlightImporter(), new LocalConversationEngine(personas), personas,
                new JsonAircraftCapabilityCatalog(Path.Combine(directory, "aircraft.json")), setupStore: preferences);
            await vm.InitializeAsync(); await vm.StartAutomaticAsync();
            Assert.True(vm.HasWorkspace); Assert.True(vm.HasRecovery); Assert.Equal("Retry connection", vm.RecoveryButton); Assert.False(vm.HasOperation); Assert.False(vm.CanChat); Assert.False(vm.NeedsSetup);
            Assert.Contains("Automatic connection is off", vm.WelcomeMessage); Assert.Equal("Live connections stopped", vm.ConnectionLabel);
            Assert.Empty(vm.Messages); Assert.Empty(vm.ServiceStatus); Assert.False(vm.StartCommand.CanExecute(null));
            Assert.Equal("Waiting for a flight plan", vm.LoadLabel);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
