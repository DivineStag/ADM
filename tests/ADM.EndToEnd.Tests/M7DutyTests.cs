using System.Collections.Immutable;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Infrastructure;
using static ADM.EndToEnd.Tests.LiveWorkflowTests;

namespace ADM.EndToEnd.Tests;

public sealed class M7DutyTests
{
    private sealed class FlightClock : IOperationalClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private static LiveDispatchCoordinator Create(ScriptedLiveProvider provider, SqliteDispatchStore store, FlightClock clock) =>
        new(provider, provider, store, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), clock);
    private static async Task Preboarding(LiveDispatchCoordinator coordinator, ScriptedLiveProvider provider)
    {
        await coordinator.SendAsync("Yes, start services", Simulator, CancellationToken.None);
        for (int i = 0; i < 12; i++)
        {
            var service = coordinator.Checkpoint!.State.Session.Services.Values.FirstOrDefault(service => service.Kind != ServiceKind.Boarding && service.State == ServiceState.Requested);
            if (service is null) break;
            provider.Set(service.Kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(service.Kind, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
        }
        await coordinator.ConfirmGateAsync(Simulator, CancellationToken.None);
    }
    private static async Task Finish(LiveDispatchCoordinator coordinator, ScriptedLiveProvider provider)
    {
        await Preboarding(coordinator, provider);
        await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
        provider.Set(ServiceKind.Boarding, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
        provider.Set(ServiceKind.Boarding, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
        await coordinator.SendAsync("Close up", Simulator, CancellationToken.None);
        foreach (string confirmation in new[] { "Confirm doors closed", "Confirm jetway / stairs clear", "Confirm ground equipment clear" })
            await coordinator.SendAsync(confirmation, Simulator, CancellationToken.None);
        Assert.Equal(WorkflowStage.Completed, coordinator.Checkpoint!.State.Session.Stage);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_sectors_survive_restart_with_independent_profiles_personas_commands_and_read_only_history(bool restart)
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        SqliteDispatchStore? store = null;
        try
        {
            var provider = new ScriptedLiveProvider(); var clock = new FlightClock(); Guid firstSession, firstPlan, dutyId; string persona;
            store = new SqliteDispatchStore(path);
            {
                var firstProfile = SampleSessionFactory.GenericProfile() with { Icao = "BAW", Revision = 2 };
                var firstResolved = new FoundationServicePlanResolver(clock).Resolve(SampleSessionFactory.GenericProfile(), firstProfile, Flight.Aircraft, FlightOperationType.FirstDeparture, new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty));
                firstResolved = firstResolved with { Services = firstResolved.Services.SetItem(ServiceKind.Cleaning, firstResolved.Services[ServiceKind.Cleaning] with { Rule = firstResolved.Services[ServiceKind.Cleaning].Rule with { Policy = ServicePolicy.Disabled } }) };
                var coordinator = Create(provider, store, clock); await Start(coordinator, firstResolved); await Finish(coordinator, provider);
                firstSession = coordinator.Checkpoint!.State.Session.Id; firstPlan = coordinator.Checkpoint.State.Session.Plan.SnapshotId;
                dutyId = coordinator.Duty!.Id; persona = coordinator.Checkpoint.State.Session.Dispatcher.Persona.Id;
                Assert.Null(await store.LoadActiveAsync(CancellationToken.None)); Assert.Single(await store.HistoryAsync(dutyId, CancellationToken.None));
                var airborne = Simulator with { OnGround = false, GroundSpeedKnots = 160, ParkingBrakeSet = false, EnginesRunning = true };
                clock.UtcNow = DateTimeOffset.UtcNow.AddSeconds(1); await coordinator.ObserveAsync(airborne with { ObservedAt = clock.UtcNow }, CancellationToken.None);
                clock.UtcNow = clock.UtcNow.AddSeconds(6); await coordinator.ObserveAsync(airborne with { ObservedAt = clock.UtcNow }, CancellationToken.None);
                Assert.Null(coordinator.Checkpoint); Assert.Null(coordinator.Flight); Assert.Equal(DutyState.EnrouteObserved, coordinator.Duty.State);
                clock.UtcNow = clock.UtcNow.AddSeconds(50);
                provider.Snapshot = provider.Snapshot with { AirportIcao = "EHAM", Stand = "B2", ReceivedAt = clock.UtcNow };
                await coordinator.ObserveAsync(Simulator with { ObservedAt = clock.UtcNow, AirportIcao = "EHAM", Stand = "B2", ParkingBrakeSet = null, EnginesRunning = null }, CancellationToken.None);
                Assert.Equal(DutyState.ArrivalCandidate, coordinator.Duty.State);
            }
            if (restart) { store.Dispose(); store = new SqliteDispatchStore(path); }
            var reopened = store; clock.UtcNow = DateTimeOffset.UtcNow;
            var next = Create(provider, reopened, clock); Assert.False(await next.RestoreAsync(CancellationToken.None));
            Assert.Equal(dutyId, next.Duty!.Id); Assert.DoesNotContain(provider.Sent, command => command.SessionId != firstSession);
            var flight = new FlightPlanSnapshot("RYR123", "EHAM", "EGLL", Flight.Aircraft, DateTimeOffset.UtcNow);
            var generic = SampleSessionFactory.GenericProfile(); var airline = generic with { Icao = "RYR", Revision = 4 };
            var plan = new FoundationServicePlanResolver(clock).Resolve(generic, airline, flight.Aircraft, FlightOperationType.Turnaround, new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty));
            plan = plan with { Services = plan.Services.SetItem(ServiceKind.Cleaning, plan.Services[ServiceKind.Cleaning] with { Rule = plan.Services[ServiceKind.Cleaning].Rule with { Policy = ServicePolicy.Disabled } }) };
            var setup = new AutomationSetup(true, true, true, "EHAM", "A20N", FlightOperationType.Turnaround);
            var capabilities = new AircraftServiceCapabilities("A20N", Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], "Synthetic M7 contract fixture");
            var dispatcher = PersonaCatalogue.LoadBundled().Select(Guid.NewGuid(), persona);
            await Assert.ThrowsAsync<InvalidOperationException>(() => next.StartAsync(flight, setup, plan, dispatcher, capabilities, Simulator, CancellationToken.None));
            await next.StartAsync(flight, setup with { SectorTransitionConfirmed = true }, plan, dispatcher, capabilities, Simulator, CancellationToken.None);
            Assert.Equal(2, next.Duty.Sectors.Length); Assert.NotEqual(firstSession, next.Checkpoint!.State.Session.Id); Assert.NotEqual(firstPlan, next.Checkpoint.State.Session.Plan.SnapshotId);
            Assert.Equal("RYR", next.Checkpoint.State.Session.Plan.AirlineIcao); Assert.Equal(4, next.Checkpoint.State.Session.Plan.ProfileRevision);
            Assert.NotEqual(persona, next.Checkpoint.State.Session.Dispatcher.Persona.Id); Assert.Empty(next.Checkpoint.State.Session.Approvals);
            Assert.DoesNotContain(next.Checkpoint.State.Session.Services.Values, service => service.State == ServiceState.Completed);
            Assert.DoesNotContain(provider.Sent, command => command.SessionId != firstSession);
            foreach (var kind in Enum.GetValues<ServiceKind>()) provider.Set(kind, ServiceState.Planned);
            await next.ReconcileAsync(Simulator, true, CancellationToken.None); await Finish(next, provider);
            var history = await reopened.HistoryAsync(dutyId, CancellationToken.None); Assert.Equal(2, history.Length);
            Assert.Equal(firstSession, history[0].Checkpoint.State.Session.Id); Assert.Equal("BAW482", history[0].Flight.Callsign);
            Assert.All(next.Duty.Sectors, sector => Assert.True(sector.HandlingCompleted));
            Assert.Equal(provider.Sent.Count, provider.Sent.Select(command => command.CommandId).Distinct().Count());
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Deboarding);
            Assert.Equal(2, provider.Sent.Select(command => command.SessionId).Distinct().Count());
            string? root = AppContext.BaseDirectory;
            while (root is not null && !File.Exists(Path.Combine(root, "ADM.sln"))) root = Directory.GetParent(root)?.FullName;
            if (root is not null)
            {
                Directory.CreateDirectory(Path.Combine(root, "artifacts"));
                File.WriteAllText(Path.Combine(root, "artifacts", $"m7-two-sector-{(restart ? "restart" : "continuous")}-evidence.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    Synthetic = true,
                    Restart = restart,
                    Sectors = next.Duty.Sectors.Select(sector => new { sector.Number, Callsign = sector.Flight.Callsign, sector.Airline, sector.ProfileRevision, Dispatcher = sector.Dispatcher.Persona.Id, sector.HandlingCompleted }),
                    Requests = provider.Sent.Count,
                    UniqueRequests = provider.Sent.Select(command => command.CommandId).Distinct().Count(),
                    ArchivedOperations = history.Length,
                    InheritedApprovals = 0,
                    DeboardingRequests = 0,
                    PushbackCommands = 0
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally { store?.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Reconnect_with_persisted_position_does_not_stop_the_connection_worker_or_replay_services()
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            var provider = new ScriptedLiveProvider();
            using (var store = new SqliteDispatchStore(path))
            {
                var original = Coordinator(provider, store); await Start(original, approveServices: false);
                await original.ObserveAsync(Simulator with { Latitude = 51.47, Longitude = -0.45 }, CancellationToken.None);
                Assert.Equal(51.47, (await store.LoadDutyAsync(CancellationToken.None))!.Latitude);
            }
            using var reopened = new SqliteDispatchStore(path); var restored = Coordinator(provider, reopened);
            Assert.True(await restored.RestoreAsync(CancellationToken.None));
            Assert.Null(restored.Duty!.LastObservedAt); Assert.Null(restored.Duty.Latitude); Assert.Null(restored.Duty.Longitude);
            var first = Simulator with { Latitude = 51.47, Longitude = -0.45 };
            await restored.ObserveAsync(first, CancellationToken.None);
            Assert.Equal(first.ObservedAt, restored.Duty.LastObservedAt); Assert.Equal(first.Latitude, restored.Duty.Latitude);
            Assert.Empty(provider.Sent); Assert.False(restored.IsArmed);
            await restored.ReconcileAsync(Simulator, true, CancellationToken.None);
            await restored.ObserveAsync(Simulator with { Latitude = 51.47, Longitude = -0.45 }, CancellationToken.None);
            Assert.True(restored.IsArmed); Assert.Empty(provider.Sent);
            Assert.Equal(QuestionKind.ServicesStart, restored.Checkpoint!.State.Question?.Kind);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Manual_chat_clear_persists_visibility_without_removing_questions_audit_or_command_deduplication()
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            using var store = new SqliteDispatchStore(path); var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator, approveServices: false); var checkpoint = coordinator.Checkpoint!;
            await coordinator.ClearChatAsync(CancellationToken.None);
            Assert.Equal(checkpoint.Messages.Length, coordinator.Checkpoint!.VisibleMessageOffset);
            Assert.Equal(checkpoint.State.Question, coordinator.Checkpoint.State.Question); Assert.Equal(checkpoint.Messages, coordinator.Checkpoint.Messages);
            var restored = Coordinator(provider, store); Assert.True(await restored.RestoreAsync(CancellationToken.None));
            Assert.Equal(checkpoint.Messages.Length, restored.Checkpoint!.VisibleMessageOffset); Assert.Empty(provider.Sent);
            await restored.ReconcileAsync(Simulator, true, CancellationToken.None); await restored.SendAsync("Yes, start services", Simulator, CancellationToken.None);
            Assert.Single(provider.Sent); Assert.True(restored.Checkpoint.Messages.Length > restored.Checkpoint.VisibleMessageOffset);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Closing_after_completed_handling_does_not_restore_the_previous_flight_or_conversation_as_active()
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db"); Guid duty;
        try
        {
            using (var store = new SqliteDispatchStore(path))
            {
                var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store); await Start(coordinator); await Finish(coordinator, provider); duty = coordinator.Duty!.Id;
                await ((IFlightPlanCache)store).SaveAsync(Flight, CancellationToken.None);
            }
            using var reopened = new SqliteDispatchStore(path); var nextProvider = new ScriptedLiveProvider(); var restored = Coordinator(nextProvider, reopened);
            Assert.False(await restored.RestoreAsync(CancellationToken.None)); Assert.Null(restored.Flight); Assert.Null(restored.Checkpoint);
            Assert.Equal(duty, restored.Duty!.Id); Assert.Equal(DutyState.ReleasedForFlight, restored.Duty.State); Assert.Empty(nextProvider.Sent);
            var archive = Assert.Single(await reopened.HistoryAsync(duty, CancellationToken.None)); Assert.NotEmpty(archive.Checkpoint.Messages);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Restarted_boarding_needs_fresh_permission_and_cannot_inherit_closure_readiness()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator, approveServices: false); await Preboarding(coordinator, provider);
            await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Boarding, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Boarding, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            await coordinator.SendAsync("Restart boarding", Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Boarding);
            Assert.Equal(QuestionKind.Boarding, coordinator.Checkpoint!.State.Question?.Kind);
            Assert.All(coordinator.Checkpoint.State.Session.Approvals, approval => Assert.True(approval.IsInvalidated));
            Assert.All(coordinator.Checkpoint.State.Releases.Values, release => Assert.False(release.IsSatisfied));
            await coordinator.SendAsync("Yes, start boarding", Simulator, CancellationToken.None);
            Assert.Equal(2, provider.Sent.Count(command => command.Kind == ServiceKind.Boarding));
            Assert.NotEqual(WorkflowStage.AwaitingClosureApproval, coordinator.Checkpoint.State.Session.Stage);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Unknown_service_cannot_restart_until_GSX_reports_fresh_availability()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator);
            for (int i = 0; i < 5 && !provider.Sent.Any(command => command.Kind == ServiceKind.Refuelling); i++)
            {
                var kind = coordinator.Checkpoint!.State.Session.Services.Values.First(service => service.State == ServiceState.Requested).Kind;
                provider.Set(kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                provider.Set(kind, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            }
            provider.Set(ServiceKind.Refuelling, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Refuelling, ServiceState.Unknown); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            int requests = provider.Sent.Count;
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SendAsync("Restart fuel", Simulator, CancellationToken.None));
            Assert.Equal(requests, provider.Sent.Count); Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Refuelling].State);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Legacy_planned_deboarding_is_skipped_on_restore_without_rewriting_the_applied_profile_or_sending_it()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var clock = new FlightClock();
            var plan = Plan(FlightOperationType.Turnaround);
            plan = plan with { Services = plan.Services.SetItem(ServiceKind.Deboarding, plan.Services[ServiceKind.Deboarding] with { Rule = new(ServicePolicy.Automatic, false, [], "Legacy plan") }) };
            Guid id = Guid.NewGuid(); var state = OperationalFactory.Create(id, Guid.NewGuid(), plan, new(id, PersonaCatalogue.LoadBundled().Select(id), clock.UtcNow), Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], []);
            var local = new LocalDispatchSession(state, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), clock);
            await store.SaveAsync(new(local.Checkpoint, Flight, "Fixture aircraft"), CancellationToken.None);
            var provider = new ScriptedLiveProvider(); var restored = Create(provider, store, clock); Assert.True(await restored.RestoreAsync(CancellationToken.None));
            Assert.Equal(ServiceState.Skipped, restored.Checkpoint!.State.Session.Services[ServiceKind.Deboarding].State);
            Assert.Equal(plan.SnapshotId, restored.Checkpoint.State.Session.Plan.SnapshotId);
            Assert.Equal(ServicePolicy.Automatic, restored.Checkpoint.State.Session.Plan.Services[ServiceKind.Deboarding].Rule.Policy);
            Assert.DoesNotContain(ServiceKind.Deboarding, restored.Checkpoint.State.SupportedServices); Assert.Empty(provider.Sent);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Externally_cancelled_fuel_can_be_explicitly_restarted_without_attesting_completion_or_replaying_old_intent()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator);
            for (int i = 0; i < 5 && !provider.Sent.Any(command => command.Kind == ServiceKind.Refuelling); i++)
            {
                var kind = coordinator.Checkpoint!.State.Session.Services.Values.First(service => service.State == ServiceState.Requested).Kind;
                provider.Set(kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                provider.Set(kind, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            }
            provider.Set(ServiceKind.Refuelling, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Refuelling, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Refuelling].State);
            await coordinator.SendAsync("Restart fuel", Simulator, CancellationToken.None);
            Assert.Equal(2, provider.Sent.Count(command => command.Kind == ServiceKind.Refuelling));
            Assert.Equal(ServiceState.Requested, coordinator.Checkpoint.State.Session.Services[ServiceKind.Refuelling].State);
            Assert.DoesNotContain(ServiceKind.Refuelling, coordinator.Checkpoint.State.ServiceCompletionChecks); Assert.False(coordinator.Checkpoint.State.FuelAttested);
            for (int i = 0; i < 20; i++) await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.True(coordinator.IsArmed); Assert.Equal(2, provider.Sent.Count(command => command.Kind == ServiceKind.Refuelling));
            Assert.Equal(2, provider.Sent.Where(command => command.Kind == ServiceKind.Refuelling).Select(command => command.IntentVersion).Distinct().Count());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
