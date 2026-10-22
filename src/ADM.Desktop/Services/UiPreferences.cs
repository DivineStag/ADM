using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using ADM.Domain;

namespace ADM.Desktop.Services;

public sealed record UiPreferences(string Theme = "Dark", bool Muted = false, bool DoNotDisturb = false,
    double Volume = 45, bool ShowServiceRail = true, bool RealismEnabled = true, int MaximumDelayMinutes = 10)
{
    public int DelayProbabilityPercent { get; init; } = 25;
    public ImmutableDictionary<string, ImmutableDictionary<ServiceKind, int>> ServiceSequences { get; init; } = ImmutableDictionary<string, ImmutableDictionary<ServiceKind, int>>.Empty;
}
public sealed record PreferencesLoad(UiPreferences Preferences, string? Warning);
public interface IUiPreferencesStore
{
    PreferencesLoad Load();
    void Save(UiPreferences preferences);
}
public sealed class JsonUiPreferencesStore(string path) : IUiPreferencesStore
{
    public PreferencesLoad Load()
    {
        try
        {
            if (!File.Exists(path)) return new(new(), null);
            var preferences = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(path));
            if (preferences is null || preferences.Theme is not ("Dark" or "Light") ||
                !double.IsFinite(preferences.Volume) || preferences.Volume is < 0 or > 100 ||
                preferences.MaximumDelayMinutes is < 0 or > 10 || preferences.DelayProbabilityPercent is < 0 or > 100)
                return new(new(), "Saved appearance settings were invalid. Defaults are in use.");
            if (preferences.ServiceSequences is null) throw new ArgumentException("Missing service sequences.");
            foreach (var phases in preferences.ServiceSequences.Values) ServiceSequence.Validate(phases);
            return new(preferences, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(new(), "Appearance settings could not be read. Defaults are in use."); }
    }
    public void Save(UiPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
