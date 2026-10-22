using ADM.Contracts;
using ADM.Domain;
using ADM.Infrastructure;
using static ADM.EndToEnd.Tests.LiveWorkflowTests;

namespace ADM.EndToEnd.Tests;

public sealed class ServiceStartWorkflowTests
{
    [Fact]
    public async Task Preparing_asks_once_and_never_requests_services_until_contextual_pilot_approval()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator, approveServices: false);
            for (int i = 0; i < 20; i++) await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(QuestionKind.ServicesStart, coordinator.Checkpoint!.State.Question?.Kind);
            Assert.Empty(provider.Sent);
            Assert.Single(coordinator.Checkpoint.Messages, message => message.SemanticKey.StartsWith("services.start.approval", StringComparison.Ordinal));
            await coordinator.SendAsync("Not yet", Simulator, CancellationToken.None);
            await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
            Assert.Empty(provider.Sent);
            await coordinator.SendAsync("Yes, start services", Simulator, CancellationToken.None);
            await coordinator.SendAsync("Yes, start services", Simulator, CancellationToken.None);
            Assert.Single(provider.Sent); Assert.Equal(ServiceKind.Water, provider.Sent[0].Kind);
            Assert.Single(coordinator.Checkpoint.Messages, message => message.SemanticKey == "services.starting");
            Assert.Equal(EvidenceSource.User, coordinator.Checkpoint.State.ServiceStartApproval?.Source);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static async Task ReachFuel(ScriptedLiveProvider provider, ADM.Application.LiveDispatchCoordinator coordinator)
    {
        foreach (var kind in new[] { ServiceKind.Water, ServiceKind.Lavatory })
        {
            provider.Set(kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(kind, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
        }
    }
    [Fact]
    public async Task Catering_waits_for_observed_fuel_start_before_its_separate_request_while_other_services_remain_serial()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator);
            Assert.Equal(new[] { ServiceKind.Water }, provider.Sent.Select(command => command.Kind));
            await ReachFuel(provider, coordinator);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Catering);
            provider.Set(ServiceKind.Refuelling, ServiceState.Requested); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(new[] { ServiceKind.Water, ServiceKind.Lavatory, ServiceKind.Refuelling, ServiceKind.Catering }, provider.Sent.Select(command => command.Kind));
            Assert.Equal(ServiceState.Requested, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Refuelling].State);
            Assert.Equal(ServiceState.Requested, coordinator.Checkpoint.State.Session.Services[ServiceKind.Catering].State);
            Assert.NotEqual(provider.Sent[2].CommandId, provider.Sent[3].CommandId);
            for (int i = 0; i < 20; i++) await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(4, provider.Sent.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Different_saved_phases_keep_fuel_and_catering_separate()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            var plan = Plan(); plan = plan with { ServicePhases = plan.ServicePhases.SetItem(ServiceKind.Catering, 4) };
            await Start(coordinator, plan); await ReachFuel(provider, coordinator);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Catering);
            provider.Set(ServiceKind.Refuelling, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Catering);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Paired_catering_still_waits_for_its_own_GSX_availability()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider();
            provider.Snapshot = provider.Snapshot with
            {
                Services = provider.Snapshot.Services.SetItem(ServiceKind.Catering,
                ScriptedLiveProvider.Status(ServiceState.Unknown) with { IsIdleUnavailable = true })
            };
            var coordinator = Coordinator(provider, store); await Start(coordinator); await ReachFuel(provider, coordinator);
            Assert.Contains(provider.Sent, command => command.Kind == ServiceKind.Refuelling);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Catering);
            provider.Set(ServiceKind.Catering, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Catering);
            provider.Set(ServiceKind.Refuelling, ServiceState.Requested); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Catering);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Routine_waiting_is_silent_in_chat_but_real_holds_and_explicit_delay_answers_remain()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store); await Start(coordinator);
            int messages = coordinator.Checkpoint!.Messages.Length;
            for (int i = 0; i < 9; i++)
            {
                provider.Snapshot = provider.Snapshot with
                {
                    Services = provider.Snapshot.Services.SetItem(ServiceKind.Water,
                    ScriptedLiveProvider.Status(ServiceState.Requested) with { Waiting = i % 2 == 0 })
                };
                await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            }
            Assert.Equal(messages, coordinator.Checkpoint.Messages.Length);
            Assert.Contains(coordinator.Checkpoint.State.Session.Holds, hold => hold.IsActive && !hold.IsSimulated);
            Assert.DoesNotContain("GSX reports", coordinator.Status, StringComparison.Ordinal);
            await coordinator.SendAsync("What's the delay?", Simulator, CancellationToken.None);
            Assert.Contains("GSX reports water waiting", coordinator.Checkpoint.Messages.Last().Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Lavatory);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Uncertain_fuel_delivery_blocks_the_paired_catering_request()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store); await Start(coordinator);
            provider.Set(ServiceKind.Water, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Water, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Lavatory, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Receipt = ReceiptStatus.Unknown;
            provider.Set(ServiceKind.Lavatory, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Refuelling].State);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Catering);
            Assert.False(coordinator.IsArmed);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Rejected_initial_request_does_not_claim_services_have_started()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider { Receipt = ReceiptStatus.Rejected }; var coordinator = Coordinator(provider, store);
            await Start(coordinator);
            Assert.DoesNotContain(coordinator.Checkpoint!.Messages, message => message.SemanticKey == "services.starting");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Unapproved_start_is_restored_without_a_request_or_an_unearned_starting_message()
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            var provider = new ScriptedLiveProvider();
            using (var store = new SqliteDispatchStore(path)) await Start(Coordinator(provider, store), approveServices: false);
            using var reopened = new SqliteDispatchStore(path); var coordinator = Coordinator(provider, reopened);
            await coordinator.RestoreAsync(CancellationToken.None); await coordinator.ReconcileAsync(Simulator, true, CancellationToken.None);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Empty(provider.Sent);
            Assert.DoesNotContain(coordinator.Checkpoint!.Messages, message => message.SemanticKey == "services.starting");
            await coordinator.SendAsync("Yes", Simulator, CancellationToken.None);
            Assert.Single(provider.Sent);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
