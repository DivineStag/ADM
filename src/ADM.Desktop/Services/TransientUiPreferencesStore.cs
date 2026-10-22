namespace ADM.Desktop.Services;

/// <summary>UI verification never changes the pilot's saved preferences.</summary>
public sealed class TransientUiPreferencesStore : IUiPreferencesStore
{
    public PreferencesLoad Load() => new(new(), null);
    public void Save(UiPreferences preferences) { }
}
