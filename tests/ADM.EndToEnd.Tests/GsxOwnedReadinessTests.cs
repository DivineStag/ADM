using ADM.Application;
using ADM.Domain;
using ADM.Infrastructure;
using static ADM.EndToEnd.Tests.LiveWorkflowTests;

namespace ADM.EndToEnd.Tests;

public sealed class GsxOwnedReadinessTests
{
    [Fact]
    public async Task Unknown_or_disagreeing_cockpit_readings_do_not_pause_handling_but_GSX_unavailability_still_blocks_requests()
    {
        string directory = DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = Coordinator(provider, store);
            await Start(coordinator, approveServices: false);
            foreach (var kind in provider.Snapshot.Services.Keys)
                provider.Snapshot = provider.Snapshot with { Services = provider.Snapshot.Services.SetItem(kind, provider.Snapshot.Services[kind] with { CanTrigger = false }) };
            var unknown = Simulator with { OnGround = null, GroundSpeedKnots = null, EnginesRunning = null, ParkingBrakeSet = null };
            await coordinator.SendAsync("Yes, start services", unknown, CancellationToken.None);
            Assert.Empty(provider.Sent); Assert.True(coordinator.IsArmed);
            foreach (var kind in provider.Snapshot.Services.Keys) provider.Set(kind, ServiceState.Planned);
            await coordinator.ObserveAsync(Simulator with { ParkingBrakeSet = false, EnginesRunning = true }, CancellationToken.None);
            Assert.True(coordinator.IsArmed); Assert.NotEmpty(provider.Sent);
            Assert.DoesNotContain(provider.Sent, command => command.Kind == ServiceKind.Boarding);
            Assert.DoesNotContain(coordinator.Checkpoint!.State.Session.Services.Values, service => service.State == ServiceState.Completed);
            int sent = provider.Sent.Count;
            await coordinator.ObserveAsync(Simulator with { AircraftTitle = "Different aircraft" }, CancellationToken.None);
            Assert.False(coordinator.IsArmed); Assert.Equal(sent, provider.Sent.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("offline")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("aircraft")]
    [InlineData("airport")]
    public void Delegating_cockpit_checks_does_not_remove_connection_identity_or_flight_binding(string condition)
    {
        var provider = new ScriptedLiveProvider();
        var sim = condition switch
        {
            "offline" => Simulator with { Connection = ConnectionState.Offline },
            "stale" => Simulator with { ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            "future" => Simulator with { ObservedAt = DateTimeOffset.UtcNow.AddMinutes(1) },
            "aircraft" => Simulator with { AircraftTitle = "Different aircraft" },
            _ => Simulator
        };
        if (condition == "airport") provider.Snapshot = provider.Snapshot with { AirportIcao = "EHAM" };
        Assert.Throws<InvalidOperationException>(() => LiveDispatchCoordinator.ValidateEnvironment(sim, provider.Snapshot, Flight, "Fixture aircraft"));
    }
}
