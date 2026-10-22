using System.Collections.Immutable;
using System.Threading.Channels;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Integrations.Gsx;

/// <summary>In-memory test double. Acceptance never generates completion evidence.</summary>
public sealed class FakeGsxGateway : IGsxGateway
{
    private readonly Channel<GsxEvent> _events = Channel.CreateUnbounded<GsxEvent>();
    private readonly object _sync = new();
    private readonly Dictionary<(Guid Session, ServiceKind Kind, int Version, bool Cancel), CommandReceipt> _receipts = [];
    private readonly List<ServiceCommand> _sent = [];

    public ImmutableArray<ServiceCommand> SentCommands { get { lock (_sync) return [.. _sent]; } }
    public GsxCapabilities Capabilities { get; } = new("FAKE GSX — no simulator", null, true,
        Enum.GetValues<ServiceKind>().ToImmutableHashSet(), ImmutableHashSet<ServiceKind>.Empty,
        ImmutableHashSet<GsxAction>.Empty);

    public Task<GsxCapabilities> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Capabilities);
    }

    public IAsyncEnumerable<GsxEvent> WatchAsync(CancellationToken cancellationToken) =>
        _events.Reader.ReadAllAsync(cancellationToken);

    public Task<CommandReceipt> RequestServiceAsync(ServiceCommand command, CancellationToken cancellationToken) =>
        Accept(command, false, cancellationToken);

    public Task<CommandReceipt> CancelServiceAsync(ServiceCommand command, CancellationToken cancellationToken) =>
        Accept(command, true, cancellationToken);

    private Task<CommandReceipt> Accept(ServiceCommand command, bool cancel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.SessionId == Guid.Empty || command.CommandId == Guid.Empty || command.PlanSnapshotId == Guid.Empty ||
            command.IntentVersion < 1 || string.IsNullOrWhiteSpace(command.PolicySource))
            return Task.FromResult(new CommandReceipt(command.CommandId, ReceiptStatus.Rejected, "Missing command identity or policy snapshot."));
        var supported = cancel ? Capabilities.CancellableServices : Capabilities.RequestableServices;
        if (!supported.Contains(command.Kind))
            return Task.FromResult(new CommandReceipt(command.CommandId, ReceiptStatus.Rejected, "Not supported by the fake capability manifest; manual intervention required."));
        lock (_sync)
        {
            var key = (command.SessionId, command.Kind, command.IntentVersion, cancel);
            if (_receipts.TryGetValue(key, out var existing)) return Task.FromResult(existing);
            var receipt = new CommandReceipt(command.CommandId, ReceiptStatus.Accepted, "Fake acceptance only; completion must be injected separately as simulation evidence.");
            _receipts.Add(key, receipt);
            _sent.Add(command);
            return Task.FromResult(receipt);
        }
    }

    public Task<CommandReceipt> ExecuteVerifiedActionAsync(ActionCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandReceipt(command.CommandId, ReceiptStatus.Rejected, "No aircraft or equipment actions verified in M0."));
    }

    public ValueTask PublishSimulatedStateAsync(Guid sessionId, ServiceKind kind, ServiceState state,
        DateTimeOffset observedAt, CancellationToken cancellationToken = default)
    {
        var evidence = new Evidence(EvidenceSource.Simulation, "Explicit fake fixture observation", observedAt);
        var metadata = new EventMetadata(Guid.NewGuid(), sessionId, observedAt, EvidenceSource.Simulation, Guid.NewGuid(), evidence);
        return _events.Writer.WriteAsync(new(metadata, kind, state), cancellationToken);
    }
}
