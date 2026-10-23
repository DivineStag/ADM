using System.Collections.Immutable;
using ADM.Application;
using ADM.Contracts;
using ADM.Domain;
using ADM.Infrastructure;
using Microsoft.Data.Sqlite;

namespace ADM.EndToEnd.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Saving_an_oversized_profile_cannot_create_an_unreadable_revision_or_replace_the_previous_one()
    {
        string directory = LiveWorkflowTests.DirectoryPath();
        try
        {
            var repository = new JsonAirlineProfileRepository(directory);
            var original = SampleSessionFactory.GenericProfile() with { Icao = "BAW", Revision = 1 };
            await repository.SaveAsync(original, CancellationToken.None);
            var operation = original.Operations[FlightOperationType.FirstDeparture];
            var oversized = original with
            {
                Revision = 2,
                Operations = original.Operations.SetItem(FlightOperationType.FirstDeparture, operation with
                {
                    Services = operation.Services.SetItem(ServiceKind.Water, operation.Services[ServiceKind.Water] with { ApplicabilityRule = new string('x', 1_048_576) })
                })
            };
            await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(oversized, CancellationToken.None));
            Assert.Equal(1, (await repository.FindAsync("BAW", CancellationToken.None))!.Revision);
            Assert.False(File.Exists(Path.Combine(directory, "BAW.json.bak")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Invalid_checkpoints_are_rejected_before_any_session_or_audit_row_is_written()
    {
        string directory = LiveWorkflowTests.DirectoryPath();
        try
        {
            using var store = new SqliteDispatchStore(Path.Combine(directory, "dispatch.db"));
            var provider = new ScriptedLiveProvider(); var coordinator = LiveWorkflowTests.Coordinator(provider, store);
            await LiveWorkflowTests.Start(coordinator, approveServices: false);
            var original = await store.LoadActiveAsync(CancellationToken.None);
            var invalid = original! with { Checkpoint = original.Checkpoint with { SchemaVersion = 2 } };
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(invalid, CancellationToken.None));
            var actual = await store.LoadActiveAsync(CancellationToken.None);
            Assert.Equal(1, actual!.Checkpoint.SchemaVersion);
            Assert.Equal(original.Checkpoint.State.Session.Id, actual.Checkpoint.State.Session.Id);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("2")]
    [InlineData("1),(1")]
    public async Task Unsupported_or_ambiguous_database_versions_are_rejected_before_schema_changes(string versions)
    {
        string directory = LiveWorkflowTests.DirectoryPath(); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "dispatch.db");
        try
        {
            using (var original = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                original.Open(); using var command = original.CreateCommand();
                command.CommandText = "CREATE TABLE schema_version(version INTEGER); INSERT INTO schema_version VALUES(" + versions + ");";
                command.ExecuteNonQuery();
            }
            using var store = new SqliteDispatchStore(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadActiveAsync(CancellationToken.None));
            using var check = new SqliteConnection($"Data Source={path};Pooling=False"); check.Open();
            using var query = check.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='sessions'";
            Assert.Equal(0L, query.ExecuteScalar());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task An_oversized_profile_falls_back_to_the_last_valid_revision()
    {
        string directory = LiveWorkflowTests.DirectoryPath();
        try
        {
            var repository = new JsonAirlineProfileRepository(directory);
            var first = SampleSessionFactory.GenericProfile() with { Icao = "BAW", Revision = 1 };
            await repository.SaveAsync(first, CancellationToken.None);
            await repository.SaveAsync(first with { Revision = 2 }, CancellationToken.None);
            using (var file = File.OpenWrite(Path.Combine(directory, "BAW.json"))) file.SetLength(1_048_577);
            Assert.Equal(1, (await repository.FindAsync("BAW", CancellationToken.None))!.Revision);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Null_and_oversized_aircraft_capability_files_are_rejected_explicitly()
    {
        string path = Path.GetTempFileName();
        try
        {
            var catalog = new JsonAircraftCapabilityCatalog(path);
            File.WriteAllText(path, """{"schemaVersion":1,"aircraft":[null]}""");
            Assert.Throws<InvalidDataException>(() => catalog.Resolve("A20N"));
            using (var file = File.OpenWrite(path)) file.SetLength(1_048_577);
            Assert.Throws<InvalidDataException>(() => catalog.Resolve("A20N"));
        }
        finally { File.Delete(path); }
    }

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
