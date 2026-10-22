using System.Collections.Immutable;
using ADM.Application;
using ADM.Domain;
using ADM.Infrastructure;

namespace ADM.EndToEnd.Tests;

public sealed class M6ProfileTests
{
    [Fact]
    public async Task Profile_transfer_roundtrips_tuning_and_overrides_and_cancelled_export_retains_old_file()
    {
        string directory = LiveWorkflowTests.DirectoryPath(); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "BAW.json");
            var profile = SampleSessionFactory.GenericProfile() with { Icao = "BAW", Realism = new(45, 3) };
            var transfer = new JsonProfileTransfer(); await transfer.WriteAsync(path, profile, CancellationToken.None);
            var imported = await transfer.ReadAsync(path, CancellationToken.None);
            Assert.Equal(profile.Realism, imported.Realism); Assert.Equal(profile.Operations.Count, imported.Operations.Count);
            string before = File.ReadAllText(path);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.WriteAsync(path, profile with { Revision = 2 }, new CancellationToken(true)));
            Assert.Equal(before, File.ReadAllText(path));
            File.WriteAllText(path, before.Replace("\"probabilityPercent\": 45", "\"probabilityPercent\": 101", StringComparison.Ordinal));
            await Assert.ThrowsAsync<ArgumentException>(() => transfer.ReadAsync(path, CancellationToken.None));
            File.WriteAllText(path, new string('x', 1_048_577));
            await Assert.ThrowsAsync<ArgumentException>(() => transfer.ReadAsync(path, CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void Airport_catalogue_selects_exact_then_generic_and_malformed_data_falls_back_to_manual_checks()
    {
        string directory = LiveWorkflowTests.DirectoryPath(); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "airports.json");
            File.WriteAllText(path, """{"schemaVersion":1,"airports":[{"schemaVersion":1,"icao":"GENERIC","preparationSeconds":20,"simulatedReadiness":false},{"schemaVersion":1,"icao":"EGLL","preparationSeconds":60,"simulatedReadiness":true}]}""");
            var catalog = new JsonAirportGateProfiles(path);
            Assert.Equal(60, catalog.Resolve("EGLL").PreparationSeconds); Assert.True(catalog.Resolve("EGLL").SimulatedReadiness);
            Assert.False(catalog.Resolve("EHAM").SimulatedReadiness);
            File.WriteAllText(path, "{broken"); Assert.Equal(AirportGateProfile.Generic, catalog.Resolve("EGLL"));
            File.WriteAllText(path, """{"schemaVersion":1,"airports":[{"schemaVersion":1,"icao":"EGLL","preparationSeconds":601,"simulatedReadiness":true}]}""");
            Assert.Equal(AirportGateProfile.Generic, catalog.Resolve("EGLL"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void Airline_tuning_cannot_extend_the_global_cap_or_enable_a_globally_disabled_simulation()
    {
        Assert.Equal(new RealismOptions(true, 80, 2), new RealismTuning(80, 7).Apply(new(true, 25, 2)));
        Assert.False(new RealismTuning(100, 10).Apply(RealismOptions.Disabled).Enabled);
        Assert.Throws<ArgumentException>(() => ProfileJsonCodec.Validate(SampleSessionFactory.GenericProfile() with { Realism = new(-1, 2) }));
    }
}
