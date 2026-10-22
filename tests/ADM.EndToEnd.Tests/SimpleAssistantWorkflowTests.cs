using System.Collections.Immutable;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Infrastructure;
using static ADM.EndToEnd.Tests.LiveWorkflowTests;

namespace ADM.EndToEnd.Tests;

public sealed class SimpleAssistantWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_menus_and_real_prompts_never_send_repeated_dispatcher_question_notifications(bool prompt)
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var provider = new ScriptedLiveProvider(); var clock = new Clock();
            var coordinator = new LiveDispatchCoordinator(provider, provider, store, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), clock);
            await Start(coordinator); int messages = coordinator.Checkpoint!.Messages.Length;
            for (int index = 0; index < 20; index++)
            {
                provider.Snapshot = provider.Snapshot with { MenuOpen = true, AwaitingInput = prompt };
                await coordinator.ObserveAsync(Simulator, CancellationToken.None);
                provider.Snapshot = provider.Snapshot with { MenuOpen = false, AwaitingInput = false };
                await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            }
            Assert.Equal(messages, coordinator.Checkpoint.Messages.Length);
            Assert.DoesNotContain(coordinator.Checkpoint.Messages, message => message.Text.Contains("GSX needs an answer", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private static async Task ReachFuel(ScriptedLiveProvider provider, LiveDispatchCoordinator coordinator)
    {
        foreach (var kind in new[] { ServiceKind.Water, ServiceKind.Lavatory })
        {
            provider.Set(kind, ServiceState.Active); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            provider.Set(kind, ServiceState.Planned); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GSX_plan_receipt_asks_once_and_never_claims_a_final_loadsheet_or_starts_without_approval(bool loadFigures)
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            var flight = Flight with { Source = FlightPlanSource.Gsx, PlannedPassengers = loadFigures ? 180 : null, PlannedZeroFuelWeightKg = loadFigures ? 64000 : null, SourceAircraftTitle = "Fixture aircraft" };
            await coordinator.StartAsync(flight, new(true, true, true, "EGLL", "A20N", FlightOperationType.FirstDeparture), Plan(), PersonaCatalogue.LoadBundled().Select(Guid.NewGuid()),
                new("A20N", Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], "Synthetic receipt fixture"), Simulator, CancellationToken.None);
            for (int i = 0; i < 30; i++) await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            var question = Assert.Single(coordinator.Checkpoint!.Messages, message => message.SemanticKey.StartsWith("services.start.gsx", StringComparison.Ordinal));
            Assert.Contains("Flight plan", question.Text); Assert.Contains("GSX", question.Text); Assert.DoesNotContain("loadsheet", question.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(loadFigures, question.Text.Contains("planned load", StringComparison.Ordinal)); Assert.Empty(provider.Sent);
            await coordinator.SendAsync("Yes", Simulator, CancellationToken.None); Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Water);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Accepted_but_unobserved_fuel_blocks_catering_and_becomes_a_scoped_recovery_action_without_auto_retry()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var provider = new ScriptedLiveProvider(); var clock = new Clock();
            var coordinator = new LiveDispatchCoordinator(provider, provider, store, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), clock);
            await Start(coordinator); await ReachFuel(provider, coordinator);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Refuelling); Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Catering);
            clock.UtcNow = clock.UtcNow.AddSeconds(21); await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.False(coordinator.IsArmed); Assert.Equal(ServiceKind.Refuelling, coordinator.StalledService);
            Assert.Equal(ServiceState.Unknown, coordinator.Checkpoint!.State.Session.Services[ServiceKind.Refuelling].State);
            Assert.Contains("hasn't reported it starting", coordinator.Status);
            for (int i = 0; i < 20; i++) await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Refuelling); Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Catering);
            await coordinator.ReconcileAsync(Simulator, true, CancellationToken.None); await coordinator.SendAsync("Restart fuel", Simulator, CancellationToken.None);
            Assert.Equal(2, provider.Sent.Count(command => command.Kind == ServiceKind.Refuelling)); Assert.False(coordinator.Checkpoint.State.FuelAttested);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task GSX_question_holds_new_calls_and_suspends_the_start_watchdog_then_catering_follows_observed_fuel_request()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db")); var provider = new ScriptedLiveProvider(); var clock = new Clock();
            var coordinator = new LiveDispatchCoordinator(provider, provider, store, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), clock);
            await Start(coordinator); await ReachFuel(provider, coordinator);
            provider.Snapshot = provider.Snapshot with { AwaitingInput = true };
            await coordinator.ObserveAsync(Simulator, CancellationToken.None); int messages = coordinator.Checkpoint!.Messages.Length;
            for (int i = 0; i < 30; i++) { clock.UtcNow = clock.UtcNow.AddSeconds(2); await coordinator.ObserveAsync(Simulator, CancellationToken.None); }
            Assert.True(coordinator.IsArmed); Assert.Null(coordinator.StalledService); Assert.Equal(messages, coordinator.Checkpoint.Messages.Length);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Catering);
            provider.Snapshot = provider.Snapshot with { AwaitingInput = false }; provider.Set(ServiceKind.Refuelling, ServiceState.Requested);
            await coordinator.ObserveAsync(Simulator, CancellationToken.None);
            Assert.Single(provider.Sent, command => command.Kind == ServiceKind.Catering); Assert.True(coordinator.IsArmed);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
