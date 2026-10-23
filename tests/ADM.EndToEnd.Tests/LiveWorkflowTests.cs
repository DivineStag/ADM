using System.Collections.Immutable;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Infrastructure;

namespace ADM.EndToEnd.Tests;

// Synthetic contract fixture only. IsFake=false exercises real-adapter guards; it is never runtime proof.
internal sealed class ScriptedLiveProvider : IGsxGateway, IGsxLiveState, IGsxRestartRecovery
{
    public GsxLiveSnapshot Snapshot { get; set; } = new(ConnectionState.Connected, 1, DateTimeOffset.UtcNow,
        Enum.GetValues<ServiceKind>().ToImmutableDictionary(kind => kind, kind => Status(ServiceState.Planned)), "EGLL", "A1", "Fixture aircraft");
    public List<ServiceCommand> Sent { get; } = [];
    public ReceiptStatus Receipt { get; set; } = ReceiptStatus.Accepted;
    public bool RestartReady { get; set; } = true;
    public int RestartChecks { get; private set; }
    public Task<bool> PrepareRestartAsync(ServiceKind service, long expectedGeneration, CancellationToken ct)
    { RestartChecks++; return Task.FromResult(RestartReady && Snapshot.Generation == expectedGeneration); }
    public static GsxServiceStatus Status(ServiceState state) => new(state, state == ServiceState.Planned, false,
        new(EvidenceSource.Gsx, "Synthetic GSX contract fixture", DateTimeOffset.UtcNow));
    public void Set(ServiceKind kind, ServiceState state) => Snapshot = Snapshot with { Services = Snapshot.Services.SetItem(kind, Status(state)) };
    public Task<GsxCapabilities> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(new GsxCapabilities("Contract fixture", "wire-1", false, Snapshot.Services.Keys.ToImmutableHashSet(), [], []));
    public async IAsyncEnumerable<GsxEvent> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
    public Task<CommandReceipt> RequestServiceAsync(ServiceCommand command, CancellationToken cancellationToken)
    { Sent.Add(command); return Task.FromResult(new CommandReceipt(command.CommandId, Receipt, "Fixture receipt")); }
    public Task<CommandReceipt> CancelServiceAsync(ServiceCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("Cancellation must remain disabled.");
    public Task<CommandReceipt> ExecuteVerifiedActionAsync(ActionCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("Release is outside M4.");
}

public sealed class LiveWorkflowTests
{
    [Fact]
    public async Task An_explicit_restart_checks_the_provider_before_creating_a_new_durable_intent()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider { Receipt = ReceiptStatus.Unknown, RestartReady = false };
            var coordinator = Coordinator(provider, store); await Start(coordinator);
            Assert.Single(provider.Sent);
            provider.Receipt = ReceiptStatus.Accepted;
            await coordinator.ReconcileAsync(Simulator, true, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SendAsync("Restart water", Simulator, CancellationToken.None));
            Assert.Single(provider.Sent); Assert.Equal(1, provider.RestartChecks);
            provider.RestartReady = true;
            await coordinator.SendAsync("Restart water", Simulator, CancellationToken.None);
            Assert.Equal(2, provider.RestartChecks); Assert.Equal(2, provider.Sent.Count);
            Assert.NotEqual(provider.Sent[0].CommandId, provider.Sent[1].CommandId);
            Assert.True(provider.Sent[1].IntentVersion > provider.Sent[0].IntentVersion);
        }
        finally { Directory.Delete(directory, true); }
    }

    internal sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    internal static SimulatorSnapshot Simulator => new(DateTimeOffset.UtcNow, ConnectionState.Connected, true, 0, true, false, "Fixture aircraft", null, null, null, EvidenceSource.SimConnect);
    internal static FlightPlanSnapshot Flight => new("BAW482", "EGLL", "EHAM", new("A20N", "A320_FAMILY"), DateTimeOffset.UtcNow);
    internal static AppliedServicePlan Plan(FlightOperationType operation = FlightOperationType.FirstDeparture, bool arrivalHandling = false)
    {
        var generic = SampleSessionFactory.GenericProfile();
        var services = generic.Operations[operation].Services.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with
        {
            Policy = !arrivalHandling && pair.Key is ServiceKind.Cleaning or ServiceKind.Deboarding ? ServicePolicy.Disabled : ServicePolicy.Automatic,
            Required = pair.Key is ServiceKind.Refuelling or ServiceKind.Boarding
        });
        generic = generic with { Operations = generic.Operations.SetItem(operation, new(services)) };
        return new FoundationServicePlanResolver(new Clock()).Resolve(generic, null, Flight.Aircraft, operation, new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty));
    }
    internal static LiveDispatchCoordinator Coordinator(ScriptedLiveProvider provider, SqliteDispatchStore store)
    {
        var catalogue = PersonaCatalogue.LoadBundled();
        return new(provider, provider, store, new LocalConversationEngine(catalogue), new Clock());
    }
    internal static async Task Start(LiveDispatchCoordinator coordinator, AppliedServicePlan? plan = null, bool adopt = false, bool approveServices = true)
    {
        plan ??= Plan();
        await coordinator.StartAsync(Flight, new(true, true, true, "EGLL", "A20N", plan.Operation, ExistingServicesBelongToFlight: adopt), plan,
            PersonaCatalogue.LoadBundled().Select(Guid.NewGuid()), new("A20N", Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], "Synthetic compatibility fixture"), Simulator, CancellationToken.None);
        if (approveServices) await coordinator.SendAsync("Yes, start services", Simulator, CancellationToken.None);
    }
    internal static string DirectoryPath() => Path.Combine(Path.GetTempPath(), "adm-m4-" + Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData(FlightOperationType.FirstDeparture)]
    [InlineData(FlightOperationType.Turnaround)]
    public async Task Complete_profile_workflow_reaches_boarding_approval_with_one_send_per_service(FlightOperationType operation)
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator, Plan(operation, true));
            for (int step = 0; step < 15; step++)
            {
                await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                var pending = coordinator.Checkpoint!.State.Session.Services.Values.FirstOrDefault(service => service.State == ServiceState.Requested);
                if (pending is null) break;
                provider.Set(pending.Kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                provider.Set(pending.Kind, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            }
            await coordinator.ConfirmGateAsync(Simulator, CancellationToken.None);
            Assert.Equal(WorkflowStage.AwaitingBoardingApproval, coordinator.Checkpoint!.State.Session.Stage);
            int sent = provider.Sent.Count;
            for (int index = 0; index < 100; index++) await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(sent, provider.Sent.Count); Assert.Equal(provider.Sent.Count, provider.Sent.Select(command => command.Kind).Distinct().Count());
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Boarding);
            Assert.Equal(sent, (await store.AttemptedAsync(coordinator.Checkpoint.State.Session.Id, CancellationToken.None)).Count);
            await coordinator.SendAsync("Yes, start boarding", Simulator, CancellationToken.None);
            await coordinator.SendAsync("Yes, start boarding", Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Boarding);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Unknown_receipt_stays_unresolved_across_restart_without_resending()
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            var provider = new ScriptedLiveProvider { Receipt = ReceiptStatus.Unknown };
            using (var store = new SqliteDispatchStore(path))
            {
                var coordinator = Coordinator(provider, store); await Start(coordinator); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Water].State);
            }
            using var reopened = new SqliteDispatchStore(path);
            var restored = Coordinator(provider, reopened); Assert.True(await restored.RestoreAsync(CancellationToken.None));
            await restored.ReconcileAsync(Simulator, true, CancellationToken.None);
            await restored.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Single(provider.Sent); Assert.Equal(ServiceState.Unknown, restored.Checkpoint!.State.Session.Services[ServiceKind.Water].State);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Initial_old_completion_requires_explicit_current_operation_review()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); provider.Set(ServiceKind.Refuelling, ServiceState.Completed);
            var coordinator = Coordinator(provider, store); await Start(coordinator);
            Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Refuelling].State);
            await coordinator.ReconcileAsync(Simulator, true, CancellationToken.None);
            Assert.Equal(ServiceState.Completed, coordinator.Checkpoint.State.Session.Services[ServiceKind.Refuelling].State);
            Assert.Equal(CompletionProvenance.VerifiedExternal, coordinator.Checkpoint.State.Session.Services[ServiceKind.Refuelling].CompletionProvenance);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Aircraft_change_and_reconnect_pause_before_any_further_send()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store); await Start(coordinator, approveServices: false);
            await coordinator.ObserveAsync(Simulator with { AircraftTitle = "Different aircraft" }, CancellationToken.None);
            Assert.False(coordinator.IsArmed); Assert.Empty(provider.Sent);
            await coordinator.ReconcileAsync(Simulator, true, CancellationToken.None);
            provider.Snapshot = provider.Snapshot with { Generation = 2 };
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.False(coordinator.IsArmed); Assert.Empty(provider.Sent);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Reconciliation_during_acknowledged_request_does_not_turn_a_stale_available_snapshot_into_a_second_request()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store); await Start(coordinator);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None); Assert.Single(provider.Sent);
            // GSX has acknowledged but has not yet published the requested/active patch.
            await coordinator.ReconcileAsync(Simulator, true, CancellationToken.None);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Single(provider.Sent); Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Water].State);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Known_rejection_can_be_replanned_after_review_but_does_not_retry_automatically()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider { Receipt = ReceiptStatus.Rejected }; var coordinator = Coordinator(provider, store); await Start(coordinator);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None); Assert.Single(provider.Sent);
            provider.Receipt = ReceiptStatus.Accepted;
            await coordinator.ReconcileAsync(Simulator, true, CancellationToken.None);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(2, provider.Sent.Count); Assert.NotEqual(provider.Sent[0].CommandId, provider.Sent[1].CommandId);
            Assert.True(provider.Sent[1].IntentVersion > provider.Sent[0].IntentVersion);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Manual_GSX_boarding_pauses_preboarding_automation_and_real_waiting_hold_cannot_be_skipped()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store); await Start(coordinator, approveServices: false);
            var water = provider.Snapshot.Services[ServiceKind.Water] with { Waiting = true };
            provider.Snapshot = provider.Snapshot with { Services = provider.Snapshot.Services.SetItem(ServiceKind.Water, water) };
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            await coordinator.SendAsync("Skip the gate delay", Simulator, CancellationToken.None);
            Assert.Contains(coordinator.Checkpoint!.State.Session.Holds, hold => !hold.IsSimulated && hold.IsActive);
            Assert.Empty(provider.Sent);
            provider.Set(ServiceKind.Boarding, ServiceState.Active);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.False(coordinator.IsArmed); Assert.Empty(provider.Sent);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Setup_attestations_and_simulator_airport_mismatch_are_enforced_before_initial_dispatch()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            var capabilities = new AircraftServiceCapabilities("A20N", Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], "Synthetic test only");
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(Flight, new(false, true, true, "EGLL", "A20N", FlightOperationType.FirstDeparture), Plan(),
                PersonaCatalogue.LoadBundled().Select(Guid.NewGuid()), capabilities, Simulator, CancellationToken.None));
            provider.Snapshot = provider.Snapshot with { AirportIcao = "KJFK" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => Start(coordinator));
            Assert.Empty(provider.Sent); Assert.Null(coordinator.Checkpoint);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Idle_unavailable_service_remains_planned_without_invented_completion_or_blocking_other_ready_services()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider();
            provider.Snapshot = provider.Snapshot with
            {
                Services = provider.Snapshot.Services.SetItem(ServiceKind.Water,
                ScriptedLiveProvider.Status(ServiceState.Unknown) with { IsIdleUnavailable = true })
            };
            var coordinator = Coordinator(provider, store); await Start(coordinator);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(ServiceState.Planned, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Water].State);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Water);
            Assert.Contains(provider.Sent, command => command.Kind == ServiceKind.Lavatory);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task GSX_reset_after_completion_requires_new_evidence_and_never_reuses_old_fuel_readiness()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); provider.Set(ServiceKind.Refuelling, ServiceState.Completed);
            var coordinator = Coordinator(provider, store); await Start(coordinator, adopt: true);
            Assert.Equal(ServiceState.Completed, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Refuelling].State);
            provider.Set(ServiceKind.Refuelling, ServiceState.Planned);
            provider.Snapshot = provider.Snapshot with { Generation = 2 };
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            await coordinator.ReconcileAsync(Simulator, true, CancellationToken.None);
            Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint.State.Session.Services[ServiceKind.Refuelling].State);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Refuelling);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
