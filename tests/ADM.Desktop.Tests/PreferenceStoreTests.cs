using System.IO;
using ADM.Desktop.Services;

namespace ADM.Desktop.Tests;

public sealed class PreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ADM-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "appearance.json");

    [Fact]
    public void Preferences_roundtrip_atomically_without_pilot_account_data()
    {
        var store = new JsonUiPreferencesStore(FilePath);
        var expected = new UiPreferences("Light", true, true, 35, false, false, 4);
        store.Save(expected);
        var loaded = store.Load();
        Assert.Equal(expected, loaded.Preferences);
        Assert.Null(loaded.Warning);
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.DoesNotContain("SimBrief", File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("null")]
    [InlineData("{\"Volume\":101}")]
    [InlineData("{\"Theme\":\"Other\"}")]
    [InlineData("{\"MaximumDelayMinutes\":11}")]
    public void Malformed_or_out_of_range_preferences_fall_back_visibly(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);
        var loaded = new JsonUiPreferencesStore(FilePath).Load();
        Assert.Equal(new UiPreferences(), loaded.Preferences);
        Assert.NotNull(loaded.Warning);
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    public void Dispose()
    {
        // Delete only files created by this test; do not recursively delete a computed directory.
        if (File.Exists(FilePath)) File.Delete(FilePath);
        if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp");
        if (Directory.Exists(_directory)) Directory.Delete(_directory);
    }
}
