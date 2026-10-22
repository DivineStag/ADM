using System.Threading.Channels;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Integrations.Sim;

public sealed class FakeSimulatorTelemetry : ISimulatorTelemetry
{
    private readonly Channel<SimulatorSnapshot> _snapshots = Channel.CreateUnbounded<SimulatorSnapshot>();

    public IAsyncEnumerable<SimulatorSnapshot> WatchAsync(CancellationToken cancellationToken) =>
        _snapshots.Reader.ReadAllAsync(cancellationToken);

    public ValueTask PublishAsync(SimulatorSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Source != EvidenceSource.Simulation)
            throw new ArgumentException("Fake telemetry must be labelled Simulation.", nameof(snapshot));
        return _snapshots.Writer.WriteAsync(snapshot, cancellationToken);
    }
}
