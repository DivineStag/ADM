using System.Collections.Immutable;
using ADM.Application;
using ADM.Contracts;
using ADM.Domain;
using ADM.Infrastructure;

namespace ADM.EndToEnd.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Profile_revisions_are_atomic_reloadable_and_recover_last_valid_revision()
    {
        string directory = LiveWorkflowTests.DirectoryPath();
        try
        {
            var repo = new JsonAirlineProfileRepository(directory);
            var profile = SampleSessionFactory.GenericProfile() with { Icao = "BAW", Revision = 1 };
            await repo.SaveAsync(profile, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveAsync(profile, CancellationToken.None));
            var revised = profile with { Revision = 2, DisplayName = "Saved preferences" };
            await repo.SaveAsync(revised, CancellationToken.None);
            Assert.Equal(2, (await new JsonAirlineProfileRepository(directory).FindAsync("BAW", CancellationToken.None))!.Revision);
            File.WriteAllText(Path.Combine(directory, "BAW.json"), "{ damaged");
            Assert.Equal(1, (await repo.FindAsync("BAW", CancellationToken.None))!.Revision);
            await Assert.ThrowsAsync<ArgumentException>(() => repo.FindAsync("../invalid", CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Crash_after_reservation_before_delivery_never_replays_uncertain_intent()
    {
        string directory = LiveWorkflowTests.DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            var provider = new ScriptedLiveProvider();
            using (var store = new SqliteDispatchStore(path))
            {
                var coordinator = LiveWorkflowTests.Coordinator(provider, store); await LiveWorkflowTests.Start(coordinator, approveServices: false);
                var local = new LocalDispatchSession(coordinator.Checkpoint!, new ADM.Conversation.LocalConversationEngine(ADM.Conversation.PersonaCatalogue.LoadBundled()),
                    new LiveWorkflowTests.Clock(), requiresReconciliation: false);
                local.Send("Yes, start services"); // Pure intent creation; no adapter call before the crash point.
                var checkpoint = local.Checkpoint; var command = Assert.Single(checkpoint.State.Commands);
                var saved = new SavedDispatch(checkpoint, LiveWorkflowTests.Flight, "Fixture aircraft");
                Assert.True(await store.TryBeginSendAsync(saved, command, CancellationToken.None));
                Assert.False(await store.TryBeginSendAsync(saved, command, CancellationToken.None));
                Assert.Empty(provider.Sent); // Simulate process death here, before calling the adapter.
            }
            using var restoredStore = new SqliteDispatchStore(path);
            var restored = LiveWorkflowTests.Coordinator(provider, restoredStore);
            Assert.True(await restored.RestoreAsync(CancellationToken.None));
            await restored.ReconcileAsync(LiveWorkflowTests.Simulator, true, CancellationToken.None);
            await restored.ObserveAsync(LiveWorkflowTests.Simulator, CancellationToken.None);
            Assert.Empty(provider.Sent); Assert.Equal(ServiceState.Unknown, restored.Checkpoint!.State.Session.Services[ServiceKind.Water].State);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Flight_cache_is_explicit_and_database_owner_lock_prevents_second_controller()
    {
        string directory = LiveWorkflowTests.DirectoryPath(); string path = Path.Combine(directory, "dispatch.db");
        try
        {
            using (var first = new SqliteDispatchStore(path))
            {
                IFlightPlanCache cache = first; await cache.SaveAsync(LiveWorkflowTests.Flight, CancellationToken.None);
                Assert.Equal("BAW482", (await cache.LoadAsync(CancellationToken.None))!.Callsign);
                using var second = new SqliteDispatchStore(path);
                await Assert.ThrowsAsync<IOException>(() => second.LoadActiveAsync(CancellationToken.None));
            }
            using var reopened = new SqliteDispatchStore(path);
            Assert.Equal("EHAM", (await ((IFlightPlanCache)reopened).LoadAsync(CancellationToken.None))!.Destination);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void Capability_matrix_fails_closed_and_unknown_aircraft_is_serial_only()
    {
        string directory = LiveWorkflowTests.DirectoryPath(); Directory.CreateDirectory(directory); string path = Path.Combine(directory, "matrix.json");
        try
        {
            var catalog = new JsonAircraftCapabilityCatalog(path);
            Assert.Empty(catalog.Resolve("A20N").ConcurrentGroups);
            File.WriteAllText(path, """{"schemaVersion":1,"aircraft":[{"icaoType":"A20N","services":["Water","Boarding"],"concurrentGroups":[["Water","Boarding"]],"evidenceReference":"Invalid fixture"}]}""");
            Assert.Throws<InvalidDataException>(() => catalog.Resolve("A20N"));
            File.WriteAllText(path, """{"schemaVersion":1,"aircraft":[{"icaoType":"A20N","services":["Water","Catering"],"concurrentGroups":[["Water","Catering"]],"evidenceReference":"Synthetic concurrency fixture only"}]}""");
            Assert.Single(catalog.Resolve("A20N").ConcurrentGroups);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("EG", "EHAM", 0)]
    [InlineData("EGLL", "EGLL", 0)]
    [InlineData("EGLL", "EHAM", 30)]
    [InlineData("EGLL", "EHAM", -2)]
    public void Invalid_or_stale_flight_is_not_applied(string origin, string destination, int ageHours)
    {
        var flight = LiveWorkflowTests.Flight with { Origin = origin, Destination = destination, CreatedAt = DateTimeOffset.UtcNow.AddHours(-ageHours) };
        Assert.Throws<ArgumentException>(() => FlightPlanValidation.Validate(flight, DateTimeOffset.UtcNow));
    }
}
