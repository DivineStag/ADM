using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Integrations.Sim;

public sealed record SimConnectData(double OnGround, double GroundSpeedKnots, double ParkingBrake,
    double EngineCount, double Engine1, double Engine2, double Engine3, double Engine4,
    double Latitude, double Longitude, string? Title)
{
    // Legacy state fields stay readable for persisted fixtures/contracts; live telemetry no longer requests them.
    public static SimConnectData FlightTracking(double onGround, double speed, double latitude, double longitude, string? title) =>
        new(onGround, speed, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, latitude, longitude, title);
}

// All members of a session, including construction and Dispose, are used on one dedicated thread.
public interface ISimConnectSession : IDisposable
{
    event Action<SimConnectData>? Data;
    void Open();
    void Dispatch(TimeSpan maximumWait);
}

public static class SimulatorNormalizer
{
    public static SimulatorSnapshot Normalize(SimConnectData data, DateTimeOffset at)
    {
        bool? engines = null;
        if (data.EngineCount is >= 1 and <= 4 && data.EngineCount == Math.Truncate(data.EngineCount))
        {
            var values = new[] { data.Engine1, data.Engine2, data.Engine3, data.Engine4 }.Take((int)data.EngineCount).Select(Boolean).ToArray();
            engines = values.Any(value => value == true) ? true : values.All(value => value == false) ? false : null;
        }
        return new(at, ConnectionState.Connected, Boolean(data.OnGround),
            double.IsFinite(data.GroundSpeedKnots) && data.GroundSpeedKnots >= 0 ? data.GroundSpeedKnots : null,
            Boolean(data.ParkingBrake), engines, string.IsNullOrWhiteSpace(data.Title) ? null : data.Title.Trim(),
            null, null, null, EvidenceSource.SimConnect)
        {
            Latitude = double.IsFinite(data.Latitude) && data.Latitude is >= -90 and <= 90 ? data.Latitude : null,
            Longitude = double.IsFinite(data.Longitude) && data.Longitude is >= -180 and <= 180 ? data.Longitude : null
        };
    }
    public static SimulatorSnapshot Disconnected(DateTimeOffset at, ConnectionState state) =>
        new(at, state, null, null, null, null, null, null, null, null, EvidenceSource.SimConnect);
    // SDK Bool values use zero for false and any finite nonzero for true, including -1.
    private static bool? Boolean(double value) => double.IsFinite(value) ? value != 0 : null;
}

/// <summary>Read-only SDK telemetry with serialized dispatch, stale-data clearing and connection recovery.</summary>
public sealed class SimConnectTelemetry(Func<ISimConnectSession>? sessionFactory = null, Action<string>? diagnostic = null,
    TimeSpan? reconnectDelay = null) : ISimulatorTelemetry
{
    private readonly Func<ISimConnectSession> _factory = sessionFactory ?? (() => new ManagedSimConnectSession());
    private readonly Action<string> _diagnostic = diagnostic ?? (_ => { });
    private readonly TimeSpan _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(2);
    private int _watching;
    public async IAsyncEnumerable<SimulatorSnapshot> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _watching, 1) != 0) throw new InvalidOperationException("SimConnect supports one telemetry consumer per adapter.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<SimulatorSnapshot>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        var worker = Task.Factory.StartNew(() => Run(channel.Writer, stop.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await foreach (var snapshot in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return snapshot;
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await worker.ConfigureAwait(false);
            Volatile.Write(ref _watching, 0);
        }
    }
    private void Run(ChannelWriter<SimulatorSnapshot> writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                writer.TryWrite(SimulatorNormalizer.Disconnected(DateTimeOffset.UtcNow, ConnectionState.Reconnecting));
                try
                {
                    using var session = _factory();
                    string? previous = null;
                    session.Data += data =>
                    {
                        var snapshot = SimulatorNormalizer.Normalize(data, DateTimeOffset.UtcNow);
                        string state = $"Flight tracking: ground={snapshot.OnGround}.";
                        if (state != previous) { _diagnostic(state); previous = state; }
                        writer.TryWrite(snapshot);
                    };
                    session.Open();
                    _diagnostic("SimConnect SDK session opened; waiting for user-aircraft telemetry.");
                    while (!cancellationToken.IsCancellationRequested) session.Dispatch(TimeSpan.FromMilliseconds(100));
                }
                catch (Exception exception) when (exception is COMException or IOException or DllNotFoundException or FileNotFoundException or BadImageFormatException or TypeLoadException or NotSupportedException)
                {
                    _diagnostic("SimConnect unavailable: " + exception.GetType().Name + ": " + exception.Message);
                    writer.TryWrite(SimulatorNormalizer.Disconnected(DateTimeOffset.UtcNow, ConnectionState.ReconciliationRequired));
                    if (exception is NotSupportedException or DllNotFoundException or FileNotFoundException or BadImageFormatException or TypeLoadException) break;
                }
                if (cancellationToken.WaitHandle.WaitOne(_reconnectDelay)) break;
            }
        }
        finally { writer.TryWrite(SimulatorNormalizer.Disconnected(DateTimeOffset.UtcNow, ConnectionState.Offline)); writer.TryComplete(); }
    }
}
