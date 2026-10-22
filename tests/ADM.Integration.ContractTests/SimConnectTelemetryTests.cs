using System.Collections.Concurrent;
using ADM.Domain;
using ADM.Integrations.Sim;

namespace ADM.Integration.ContractTests;

public sealed class SimConnectTelemetryTests
{
    private static SimConnectData Data => new(1, 0.1, 1, 2, 0, 0, 1, 1, 51.47, -0.45, " Test aircraft ");
    [Fact]
    public void Normalizer_does_not_invent_airport_stand_callsign_or_engine_state()
    {
        var snapshot = SimulatorNormalizer.Normalize(Data, DateTimeOffset.UnixEpoch);
        Assert.True(snapshot.OnGround); Assert.True(snapshot.ParkingBrakeSet); Assert.False(snapshot.EnginesRunning);
        Assert.Equal(0.1, snapshot.GroundSpeedKnots); Assert.Equal("Test aircraft", snapshot.AircraftTitle);
        Assert.Null(snapshot.AirportIcao); Assert.Null(snapshot.Stand); Assert.Null(snapshot.AtcCallsign);
        Assert.Equal(EvidenceSource.SimConnect, snapshot.Source);
        var invalid = SimulatorNormalizer.Normalize(Data with { OnGround = double.NaN, GroundSpeedKnots = double.NaN, ParkingBrake = double.NaN, EngineCount = 0, Latitude = 91, Longitude = double.PositiveInfinity }, DateTimeOffset.UnixEpoch);
        Assert.Null(invalid.OnGround); Assert.Null(invalid.GroundSpeedKnots); Assert.Null(invalid.ParkingBrakeSet); Assert.Null(invalid.EnginesRunning);
        Assert.Null(invalid.Latitude); Assert.Null(invalid.Longitude);
    }
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    public void Engine_state_uses_only_the_reported_engine_count(double first, double second, bool expected) =>
        Assert.Equal(expected, SimulatorNormalizer.Normalize(Data with { Engine1 = first, Engine2 = second }, DateTimeOffset.UnixEpoch).EnginesRunning);

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    public void SDK_boolean_true_accepts_every_finite_nonzero_value(double value)
    {
        var state = SimulatorNormalizer.Normalize(Data with { OnGround = value, ParkingBrake = value, Engine1 = value }, DateTimeOffset.UnixEpoch);
        Assert.True(state.OnGround); Assert.True(state.ParkingBrakeSet); Assert.True(state.EnginesRunning);
    }
    [Fact]
    public void Live_tracking_keeps_identity_position_and_flight_phase_without_claiming_cockpit_brake_or_engine_state()
    {
        var data = SimConnectData.FlightTracking(1, 0.1, 51.47, -0.45, "FenixA319 CFM WF HD");
        var state = SimulatorNormalizer.Normalize(data, DateTimeOffset.UnixEpoch);
        Assert.True(state.OnGround); Assert.Equal(0.1, state.GroundSpeedKnots);
        Assert.Equal("FenixA319 CFM WF HD", state.AircraftTitle); Assert.Equal(51.47, state.Latitude);
        Assert.Null(state.ParkingBrakeSet); Assert.Null(state.EnginesRunning);
    }

    private sealed class Session(ConcurrentBag<int> threads, bool fail) : ISimConnectSession
    {
        private bool _published;
        public event Action<SimConnectData>? Data;
        public void Open() => threads.Add(Environment.CurrentManagedThreadId);
        public void Dispatch(TimeSpan maximumWait)
        {
            threads.Add(Environment.CurrentManagedThreadId);
            if (!_published) { _published = true; Data?.Invoke(SimConnectTelemetryTests.Data); return; }
            if (fail) throw new IOException("Test disconnect");
            Thread.Sleep(5);
        }
        public void Dispose() => threads.Add(Environment.CurrentManagedThreadId);
    }
    [Fact]
    public async Task Disconnect_clears_stale_values_reconnects_and_all_sdk_calls_use_one_thread()
    {
        var threads = new ConcurrentBag<int>(); int count = 0;
        var adapter = new SimConnectTelemetry(() => new Session(threads, Interlocked.Increment(ref count) == 1), reconnectDelay: TimeSpan.FromMilliseconds(10));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var snapshots = new List<ADM.Contracts.SimulatorSnapshot>();
        await foreach (var snapshot in adapter.WatchAsync(stop.Token))
        {
            snapshots.Add(snapshot);
            if (snapshots.Count(item => item.Connection == ConnectionState.Connected) == 2) break;
        }
        var lost = Assert.Single(snapshots, item => item.Connection == ConnectionState.ReconciliationRequired);
        Assert.Null(lost.GroundSpeedKnots); Assert.Null(lost.OnGround); Assert.Null(lost.AircraftTitle); Assert.Null(lost.Latitude);
        Assert.Equal(2, count); Assert.Single(threads.Distinct());
    }
}
