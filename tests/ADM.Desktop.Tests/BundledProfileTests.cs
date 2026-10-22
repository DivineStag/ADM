using System.IO;
using ADM.Application;
using ADM.Infrastructure;
namespace ADM.Desktop.Tests;

public sealed class BundledProfileTests
{
    [Fact]
    public async Task All_supplied_profiles_are_valid_and_available_on_a_clean_install_without_writing_personal_data()
    {
        string target = Path.Combine(Path.GetTempPath(), "adm-defaults-" + Guid.NewGuid().ToString("N"));
        var repository = new JsonAirlineProfileRepository(target, bundledDirectory: Path.Combine(AppContext.BaseDirectory, "Defaults", "Profiles"));
        var profiles = await repository.ListAsync(CancellationToken.None);
        Assert.Equal(25, profiles.Count); Assert.False(Directory.Exists(target));
        var identity = new AirlineIdentityResolver(["BAW", "RYR", "UAL"]);
        foreach (var profile in profiles) { ProfileJsonCodec.Validate(profile); identity.RegisterProfileDesignator(profile.Icao); }
        Assert.Equal("EZY", identity.Resolve(new("EZY456", null, null, false), null).Icao);
        Assert.Equal("TOM", identity.Resolve(new("TOM123", null, null, false), null).Icao);
        Assert.Equal("GENERIC", identity.Resolve(new("U2456", null, null, false), null).Icao);
    }
    [Fact]
    public async Task Saving_a_bundled_default_creates_a_user_override_and_an_update_never_overwrites_it()
    {
        string target = Path.Combine(Path.GetTempPath(), "adm-defaults-" + Guid.NewGuid().ToString("N"));
        try
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "Defaults", "Profiles");
            var repository = new JsonAirlineProfileRepository(target, bundledDirectory: bundled);
            var original = (await repository.FindAsync("EZY", CancellationToken.None))!;
            var changed = original with { Revision = original.Revision + 1, DisplayName = "My preferences" };
            await repository.SaveAsync(changed, CancellationToken.None);
            var reopened = new JsonAirlineProfileRepository(target, bundledDirectory: bundled);
            Assert.Equal("My preferences", (await reopened.FindAsync("EZY", CancellationToken.None))!.DisplayName);
            File.WriteAllText(Path.Combine(target, "EZY.json"), "broken");
            Assert.Equal(original.Revision, (await reopened.FindAsync("EZY", CancellationToken.None))!.Revision);
        }
        finally { if (Directory.Exists(target)) Directory.Delete(target, true); }
    }
}
