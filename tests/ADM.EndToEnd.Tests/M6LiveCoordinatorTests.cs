using System.Collections.Immutable;
using ADM.Application;
using ADM.Conversation;
using ADM.Domain;
using ADM.Infrastructure;
using ADM.Simulation;
using static ADM.EndToEnd.Tests.LiveWorkflowTests;

namespace ADM.EndToEnd.Tests;

public sealed class M6LiveCoordinatorTests
{
    private sealed class AdjustableClock : ADM.Contracts.IOperationalClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private static LiveDispatchCoordinator Create(ScriptedLiveProvider provider, SqliteDispatchStore store, AdjustableClock clock) =>
        new(provider, provider, store, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), clock, realismEngine: new SeededDisruptionEngine());
    private static async Task Begin(LiveDispatchCoordinator coordinator)
    {
        var plan = Plan(FlightOperationType.Turnaround);
        await coordinator.StartAsync(Flight, new(true, true, true, "EGLL", "A20N", FlightOperationType.Turnaround)
        { Realism = new(true, 100, 4), GateProfile = AirportGateProfile.Generic }, plan,
            PersonaCatalogue.LoadBundled().Select(Guid.NewGuid()), new("A20N", Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], "Synthetic contract fixture"), Simulator, CancellationToken.None);
        await coordinator.SendAsync("Yes, start services", Simulator, CancellationToken.None);
    }
    [Fact]
    public async Task Live_worker_continues_services_during_simulated_hold_and_skip_restores_checked_gate_without_bypassing_boarding_approval()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Create(provider, store, new()); await Begin(coordinator);
            Assert.Equal(DisruptionStatus.Active, coordinator.Checkpoint!.State.Realism!.Incident!.Status);
            for (int step = 0; step < 10; step++)
            {
                var service = coordinator.Checkpoint.State.Session.Services.Values.FirstOrDefault(service => service.Kind != ServiceKind.Boarding && service.State == ServiceState.Requested);
                if (service is null) break;
                provider.Set(service.Kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                provider.Set(service.Kind, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            }
            Assert.Equal(4, provider.Sent.Count);
            await coordinator.ConfirmGateAsync(Simulator, CancellationToken.None);
            Assert.Equal(GateReadinessState.Held, coordinator.Checkpoint.State.Session.Gate.State);
            Assert.Equal(GateProvenance.PilotAttested, coordinator.Checkpoint.State.Realism.GateBeforeHold!.Provenance);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Boarding);
            await coordinator.SendAsync("Skip the gate delay", Simulator, CancellationToken.None);
            Assert.Equal(DisruptionStatus.Skipped, coordinator.Checkpoint.State.Realism.Incident.Status);
            Assert.Equal(QuestionKind.Boarding, coordinator.Checkpoint.State.Question?.Kind);
            Assert.Equal(GateProvenance.PilotAttested, coordinator.Checkpoint.State.Session.Gate.Provenance);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Boarding);
            await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Boarding);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Owned_simulated_gate_does_not_authorize_restoring_simulated_service_completion_as_live()
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            var provider = new ScriptedLiveProvider(); var clock = new AdjustableClock();
            using (var store = new SqliteDispatchStore(path))
            {
                var coordinator = Create(provider, store, clock); await Begin(coordinator);
                var checkpoint = coordinator.Checkpoint!;
                var water = checkpoint.State.Session.Services[ServiceKind.Water] with
                {
                    State = ServiceState.Completed,
                    CompletionProvenance = CompletionProvenance.Simulation,
                    LastEvidence = new(EvidenceSource.Simulation, "Deliberately invalid live fixture completion", clock.UtcNow)
                };
                checkpoint = checkpoint with { State = checkpoint.State with { Session = checkpoint.State.Session with { Services = checkpoint.State.Session.Services.SetItem(ServiceKind.Water, water) } } };
                await store.SaveAsync(new(checkpoint, Flight, "Fixture aircraft"), CancellationToken.None);
            }
            using var reopened = new SqliteDispatchStore(path); var restored = Create(provider, reopened, clock);
            await Assert.ThrowsAsync<InvalidDataException>(() => restored.RestoreAsync(CancellationToken.None));
            Assert.False(restored.IsArmed);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task SQLite_restart_preserves_root_and_expiry_and_requires_new_gate_evidence()
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            var provider = new ScriptedLiveProvider(); var clock = new AdjustableClock(); Guid root; DateTimeOffset ends;
            using (var store = new SqliteDispatchStore(path))
            {
                var coordinator = Create(provider, store, clock); await Begin(coordinator);
                provider.Set(ServiceKind.Water, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                await coordinator.ConfirmGateAsync(Simulator, CancellationToken.None);
                root = coordinator.Checkpoint!.State.Realism!.Incident!.Id; ends = coordinator.Checkpoint.State.Realism.Incident.ExpectedResolution;
            }
            clock.UtcNow = ends.AddSeconds(1);
            using var reopened = new SqliteDispatchStore(path); var restored = Create(provider, reopened, clock);
            await restored.RestoreAsync(CancellationToken.None);
            Assert.False(restored.IsArmed);
            await restored.ReconcileAsync(Simulator, true, CancellationToken.None);
            Assert.Equal(root, restored.Checkpoint!.State.Realism!.Incident!.Id);
            Assert.Equal(ends, restored.Checkpoint.State.Realism.Incident.ExpectedResolution);
            Assert.Equal(DisruptionStatus.Resolved, restored.Checkpoint.State.Realism.Incident.Status);
            Assert.NotEqual(GateReadinessState.Ready, restored.Checkpoint.State.Session.Gate.State);
            Assert.Single(restored.Checkpoint.Messages, message => message.SemanticKey.StartsWith("disruption.", StringComparison.Ordinal) && !message.SemanticKey.StartsWith("disruption.resolved", StringComparison.Ordinal));
            await restored.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Water);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
