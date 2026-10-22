using ADM.Contracts;
using ADM.Domain;
using ADM.Integrations.Gsx;
using ADM.Integrations.Sim;

namespace ADM.Integration.ContractTests;

public sealed class FakeAdapterTests
{
    private static ServiceCommand Command(Guid? sessionId = null) => new(Guid.NewGuid(), sessionId ?? Guid.NewGuid(),
        ServiceKind.Catering, 1, Guid.NewGuid(), "GENERIC revision 1");

    [Fact]
    public async Task Duplicate_intent_is_accepted_once_even_with_different_command_id()
    {
        var gateway = new FakeGsxGateway();
        var command = Command();
        var receipts = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => gateway.RequestServiceAsync(
            command with { CommandId = Guid.NewGuid() }, CancellationToken.None)));
        Assert.Single(gateway.SentCommands);
        Assert.All(receipts, receipt => Assert.Equal(receipts[0], receipt));
    }

    [Fact]
    public async Task Same_service_in_new_sector_is_independent()
    {
        var gateway = new FakeGsxGateway();
        await gateway.RequestServiceAsync(Command(), CancellationToken.None);
        await gateway.RequestServiceAsync(Command(), CancellationToken.None);
        Assert.Equal(2, gateway.SentCommands.Length);
    }

    [Fact]
    public async Task Accepted_receipt_emits_no_service_state_observation()
    {
        var gateway = new FakeGsxGateway();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var watcher = gateway.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        var nextEvent = watcher.MoveNextAsync().AsTask();
        var receipt = await gateway.RequestServiceAsync(Command(), cancellation.Token);
        Assert.Equal(ReceiptStatus.Accepted, receipt.Status);
        Assert.False(nextEvent.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => nextEvent);
    }

    [Fact]
    public async Task Unsupported_cancel_and_equipment_actions_remain_rejected()
    {
        var gateway = new FakeGsxGateway();
        Assert.Equal(ReceiptStatus.Rejected, (await gateway.CancelServiceAsync(Command(), CancellationToken.None)).Status);
        Assert.Equal(ReceiptStatus.Rejected, (await gateway.ExecuteVerifiedActionAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), GsxAction.CloseSupportedDoors), CancellationToken.None)).Status);
        Assert.Empty(gateway.SentCommands);
        var capabilities = await gateway.ProbeAsync(CancellationToken.None);
        Assert.True(capabilities.IsFake);
        Assert.Null(capabilities.VerifiedApiVersion);
    }

    [Fact]
    public async Task Completion_is_only_published_by_explicit_simulation_observation()
    {
        var gateway = new FakeGsxGateway();
        var command = Command();
        var receipt = await gateway.RequestServiceAsync(command, CancellationToken.None);
        Assert.Equal(ReceiptStatus.Accepted, receipt.Status);
        await gateway.PublishSimulatedStateAsync(command.SessionId, command.Kind, ServiceState.Completed, DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var watcher = gateway.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await watcher.MoveNextAsync());
        Assert.Equal(EvidenceSource.Simulation, watcher.Current.Metadata.Evidence!.Source);
        Assert.Equal(command.SessionId, watcher.Current.Metadata.SessionId);
    }

    [Fact]
    public async Task Watchers_and_requests_honour_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var gateway = new FakeGsxGateway();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.RequestServiceAsync(Command(), cancellation.Token));
        await using var watcher = gateway.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await watcher.MoveNextAsync());
    }

    [Fact]
    public async Task Fake_simulator_never_impersonates_real_simconnect_evidence()
    {
        var simulator = new FakeSimulatorTelemetry();
        var snapshot = new SimulatorSnapshot(DateTimeOffset.UtcNow, ConnectionState.Offline, null, null, null,
            null, null, null, null, null, EvidenceSource.Simulation);
        await simulator.PublishAsync(snapshot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var watcher = simulator.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await watcher.MoveNextAsync());
        Assert.Equal(snapshot, watcher.Current);
        await Assert.ThrowsAsync<ArgumentException>(async () => await simulator.PublishAsync(snapshot with { Source = EvidenceSource.SimConnect }));
    }

}
