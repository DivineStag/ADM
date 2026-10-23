using System.Collections.Immutable;
using ADM.Application;
using ADM.Domain;
using ADM.Infrastructure;
using static ADM.EndToEnd.Tests.LiveWorkflowTests;

namespace ADM.EndToEnd.Tests;

public sealed class M5WorkflowTests
{
    private static async Task FinishPreboarding(LiveDispatchCoordinator coordinator, ScriptedLiveProvider provider)
    {
        for (int step = 0; step < 12; step++)
        {
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            var current = coordinator.Checkpoint!.State.Session.Services.Values.FirstOrDefault(service => service.Kind != ServiceKind.Boarding && service.State == ServiceState.Requested);
            if (current is null) break;
            provider.Set(current.Kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(current.Kind, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
        }
        await coordinator.ConfirmGateAsync(Simulator, CancellationToken.None);
    }
    [Fact]
    public async Task Routine_active_to_available_cycle_advances_without_confirmation_or_claiming_verified_completion()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Water, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            await coordinator.SendAsync("Skip lavatory", Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Water, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            var water = coordinator.Checkpoint!.State.Session.Services[ServiceKind.Water];
            Assert.Equal(ServiceState.CycleEnded, water.State); Assert.Equal(CompletionProvenance.Unknown, water.CompletionProvenance);
            Assert.True(coordinator.IsArmed); Assert.NotEqual(QuestionKind.ServiceCompletion, coordinator.Checkpoint.State.Question?.Kind);
            Assert.Contains(provider.Sent, command => command.Kind == ServiceKind.Refuelling);
            for (int i = 0; i < 20; i++) await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Water);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData(ServiceKind.Refuelling)]
    [InlineData(ServiceKind.Boarding)]
    public async Task Fuel_and_boarding_cannot_complete_from_returning_idle_but_support_explicit_attestation(ServiceKind kind)
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator);
            if (kind == ServiceKind.Boarding)
            { await FinishPreboarding(coordinator, provider); await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None); }
            else
            {
                await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                await coordinator.SendAsync("Skip lavatory", Simulator, CancellationToken.None);
                provider.Set(ServiceKind.Water, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                provider.Set(ServiceKind.Water, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            }
            Assert.Contains(provider.Sent, command => command.Kind == kind);
            provider.Set(kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(kind, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint!.State.Session.Services[kind].State);
            Assert.Equal(QuestionKind.ServiceCompletion, coordinator.Checkpoint.State.Question?.Kind);
            await coordinator.SendAsync("Confirm service complete", Simulator, CancellationToken.None);
            Assert.Equal(ServiceState.Completed, coordinator.Checkpoint.State.Session.Services[kind].State);
            Assert.Equal(CompletionProvenance.PilotAttested, coordinator.Checkpoint.State.Session.Services[kind].CompletionProvenance);
            Assert.Single(provider.Sent, command => command.Kind == kind);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Late_boarding_completion_resolves_its_check_without_a_manual_answer()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator); await FinishPreboarding(coordinator, provider);
            await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Boarding, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Boarding, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(QuestionKind.ServiceCompletion, coordinator.Checkpoint!.State.Question?.Kind);
            provider.Set(ServiceKind.Boarding, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(WorkflowStage.AwaitingClosureApproval, coordinator.Checkpoint.State.Session.Stage);
            Assert.Equal(CompletionProvenance.VerifiedExternal, coordinator.Checkpoint.State.Session.Services[ServiceKind.Boarding].CompletionProvenance);
            Assert.Empty(coordinator.Checkpoint.State.ServiceCompletionChecks);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Manual_routine_restart_invalidates_boarding_question_and_is_not_duplicated_by_ADM()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator); await FinishPreboarding(coordinator, provider);
            provider.Set(ServiceKind.Water, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Boarding);
            provider.Set(ServiceKind.Water, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(QuestionKind.Boarding, coordinator.Checkpoint!.State.Question?.Kind);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Water);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Boarding_and_signoff_require_separate_approvals_without_physical_release_actions()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator); await FinishPreboarding(coordinator, provider);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Boarding);
            await coordinator.SendAsync("Please close the doors", Simulator, CancellationToken.None);
            Assert.Empty(coordinator.Checkpoint!.State.Session.Approvals.Where(approval => approval.Kind == ApprovalKind.Closure));
            await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Boarding, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.True(coordinator.IsArmed);
            Assert.Equal(WorkflowStage.Boarding, coordinator.Checkpoint.State.Session.Stage);
            provider.Set(ServiceKind.Boarding, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Equal(WorkflowStage.AwaitingClosureApproval, coordinator.Checkpoint.State.Session.Stage);
            Assert.All(coordinator.Checkpoint.State.Releases.Values, release => Assert.False(release.IsSatisfied));
            await coordinator.SendAsync("Please close the doors", Simulator, CancellationToken.None);
            Assert.Equal(WorkflowStage.Completed, coordinator.Checkpoint.State.Session.Stage);
            Assert.All(coordinator.Checkpoint.State.Releases.Values, release => Assert.False(release.IsSatisfied));
            Assert.Null(coordinator.Checkpoint.State.Question);
            Assert.DoesNotContain(coordinator.Checkpoint.State.Commands, command => command.Kind == OperationalCommandKind.ReleaseEquipment);
            Assert.Contains(coordinator.Checkpoint.Messages, message => message.Text.Contains("safe flight", StringComparison.OrdinalIgnoreCase));
            int sent = provider.Sent.Count;
            await coordinator.ObserveAsync(Simulator with { EnginesRunning = true, ParkingBrakeSet = false, OnGround = false }, CancellationToken.None);
            await coordinator.SendAsync("Are we ready?", Simulator, CancellationToken.None);
            Assert.Equal(sent, provider.Sent.Count); Assert.Equal(WorkflowStage.Completed, coordinator.Checkpoint.State.Session.Stage);
            Assert.Contains(coordinator.Checkpoint.Messages, message => message.Text.Contains("Pushback", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Restart_before_signoff_preserves_boarding_history_and_requires_a_fresh_go_ahead()
    {
        string directory = DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            var provider = new ScriptedLiveProvider();
            using (var store = new SqliteDispatchStore(path))
            {
                var coordinator = Coordinator(provider, store);
                await Start(coordinator); await FinishPreboarding(coordinator, provider);
                await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
                provider.Set(ServiceKind.Boarding, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            }
            int sent = provider.Sent.Count;
            using var reopened = new SqliteDispatchStore(path);
            var restored = Coordinator(provider, reopened); Assert.True(await restored.RestoreAsync(CancellationToken.None));
            Assert.False(restored.IsArmed);
            Assert.All(restored.Checkpoint!.State.Releases.Values, release => Assert.False(release.IsSatisfied));
            await restored.ReconcileAsync(Simulator, true, CancellationToken.None);
            await restored.SendAsync("Confirm doors closed", Simulator, CancellationToken.None);
            Assert.False(restored.Checkpoint.State.Releases[ReleaseAction.Doors].IsSatisfied);
            await restored.SendAsync("Close up", Simulator, CancellationToken.None);
            Assert.Equal(WorkflowStage.Completed, restored.Checkpoint.State.Session.Stage);
            Assert.Equal(sent, provider.Sent.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Disconnect_after_signoff_does_not_reopen_handling_or_invent_equipment_completion()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator); await FinishPreboarding(coordinator, provider);
            await coordinator.SendAsync("Start boarding", Simulator, CancellationToken.None);
            provider.Set(ServiceKind.Boarding, ServiceState.Completed); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            await coordinator.SendAsync("Close up", Simulator, CancellationToken.None);
            await coordinator.SendAsync("Confirm doors closed", Simulator, CancellationToken.None);
            Assert.All(coordinator.Checkpoint!.State.Releases.Values, release => Assert.False(release.IsSatisfied));
            await coordinator.ObserveAsync(Simulator with { Connection = ConnectionState.Offline }, CancellationToken.None);
            await coordinator.SendAsync("Confirm clear", Simulator, CancellationToken.None);
            Assert.All(coordinator.Checkpoint!.State.Releases.Values, release => Assert.False(release.IsSatisfied));
            Assert.Equal(WorkflowStage.Completed, coordinator.Checkpoint.State.Session.Stage);
            Assert.False(coordinator.IsArmed);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
