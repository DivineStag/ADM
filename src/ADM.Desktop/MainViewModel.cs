using ADM.Contracts;
using ADM.Conversation;
using ADM.Desktop.Presentation;
using ADM.Desktop.Services;

namespace ADM.Desktop;

public enum AppPage { Chat, Profiles, Flight, Settings }

public sealed class MainViewModel : ObservableObject
{
    private readonly INotificationPlayer _sound;
    private AppPage _selectedPage;
    private string _theme;
    private bool _muted;
    private bool _doNotDisturb;
    private double _volume;
    private bool _realismEnabled;
    private int _maximumDelayMinutes;
    private int _delayProbabilityPercent;
    private string _soundStatus = "Your ADM message sound, played locally.";
    private string _profileSaveStatus = "";
    private bool _hadLiveOperation;

    public MainViewModel(IOperationalClock clock, IServicePlanResolver resolver, INotificationPlayer sound, IUiPreferencesStore preferencesStore,
        IConversationEngine conversation, PersonaCatalogue personas, LiveOperationsViewModel? live = null, IProfileTransfer? profileTransfer = null, IProfileFileDialogs? profileDialogs = null, ISupportReportExporter? supportReports = null, GitHubUpdates? updates = null)
    {
        _sound = sound;
        PreferencesLoad loaded = preferencesStore.Load();
        var preferences = loaded.Preferences;
        _theme = preferences.Theme;
        _muted = preferences.Muted || preferences.DoNotDisturb;
        _doNotDisturb = false;
        _volume = preferences.Volume;
        _realismEnabled = preferences.RealismEnabled;
        _maximumDelayMinutes = preferences.MaximumDelayMinutes;
        _delayProbabilityPercent = preferences.DelayProbabilityPercent;
        PreferencesWarning = loaded.Warning ?? "Appearance and notification preferences save when ADM closes.";
        Profiles = new(resolver, preferences.ServiceSequences);
        Live = live;
        Updates = new(updates ?? new GitHubUpdates(new System.Net.Http.HttpClient(), "", "0.8.0-beta.4"),
            () => Live?.HandlingActive == true);
        if (Live is not null) Live.RealismPreferences = () => new(RealismEnabled, DelayProbabilityPercent, MaximumDelayMinutes);
        ImportProfileCommand = new(async () =>
        {
            if (profileTransfer is null || profileDialogs?.ChooseImport() is not { } path) return;
            Profiles.StageImportedProfile(await profileTransfer.ReadAsync(path, CancellationToken.None));
            ProfileSaveStatus = "Imported for review. Save profile to use these preferences on future flights.";
        }, exception => ProfileSaveStatus = "Profile import failed: " + exception.Message);
        ExportProfileCommand = new(async () =>
        {
            if (Profiles.EditedProfile is not { } profile || profileTransfer is null || profileDialogs?.ChooseExport(profile.Icao) is not { } path) return;
            await profileTransfer.WriteAsync(path, profile, CancellationToken.None); ProfileSaveStatus = "Handling profile exported.";
        }, exception => ProfileSaveStatus = "Profile export failed: " + exception.Message, () => Profiles.EditedProfile is not null && Profiles.SequenceError.Length == 0);
        if (Live is not null)
        {
            Live.ImportantMessage += PlayIncoming;
            Live.FlightEntryRequested += () => SelectedPage = AppPage.Flight;
            Live.FlightProfileChanged += Profiles.SelectFlightProfile;
        }
        ExportDiagnosticsCommand = new(async () =>
        {
            if (supportReports is null || Live is null) return;
            SupportStatus = await supportReports.ExportAsync(Live.SupportSnapshot(), CancellationToken.None) ? "Support report saved. Nothing was sent." : "Report not saved.";
        }, _ => SupportStatus = "The report could not be saved. Try a different folder.");
        SaveProfileCommand = new(async () =>
        {
            if (Live is null || Profiles.EditedProfile is null) return;
            ProfileSaveStatus = "Saving handling preferences…";
            await Live.SaveProfileAsync(Profiles.EditedProfile);
            Profiles.SaveSequenceCommand.Execute(null);
            Profiles.LoadProfiles(Live.SavedProfiles);
            ProfileSaveStatus = "Handling profile and service order saved for future operations.";
        }, exception => { ProfileSaveStatus = "Profile was not saved: " + exception.Message; }, () => Live is not null && Profiles.SequenceError.Length == 0);
        Profiles.PropertyChanged += (_, _) => { SaveProfileCommand.Refresh(); ExportProfileCommand.Refresh(); };
        if (Live is not null) Live.PropertyChanged += (_, _) =>
        {
            Updates.Refresh();
            bool showPreparedChat = Live.HasOperation && !_hadLiveOperation;
            _hadLiveOperation = Live.HasOperation;
            if (showPreparedChat) SelectedPage = AppPage.Chat;
            foreach (string name in new[] { nameof(IsLive), nameof(ShowDemoChat), nameof(ShowLiveChat), nameof(HeaderName), nameof(HeaderInitials), nameof(HeaderFlight), nameof(HeaderStage), nameof(HeaderGate), nameof(HeaderPersona), nameof(ConnectionText), nameof(ShowDemoRecovery) }) Raise(name);
        };
        Demo = new(clock, PlayIncoming, conversation, personas, () => Profiles.SavedPhases("BAW", ADM.Domain.FlightOperationType.Turnaround));
        Demo.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Demo.IsInterrupted)) Raise(nameof(PreviewDisconnected));
            if (!IsLive) foreach (string name in new[] { nameof(HeaderName), nameof(HeaderInitials), nameof(HeaderStage), nameof(HeaderGate), nameof(HeaderPersona), nameof(ShowDemoRecovery) }) Raise(name);
        };
        NavigateCommand = new(page => { if (page is AppPage target) SelectedPage = target; });
        PreviewSoundCommand = new(_ => PlayIncoming());
        _sound.PlaybackFailed += (_, message) => SoundStatus = message;
    }
    private string _supportStatus = "";
    public string SupportStatus { get => _supportStatus; private set => Set(ref _supportStatus, value); }
    public AsyncRelayCommand ExportDiagnosticsCommand { get; }
    public UpdatesViewModel Updates { get; }
    public event EventHandler? ThemeChanged;
    public DemoSessionViewModel Demo { get; }
    public ProfileEditorViewModel Profiles { get; }
    public LiveOperationsViewModel? Live { get; }
    public AsyncRelayCommand SaveProfileCommand { get; }
    public AsyncRelayCommand ImportProfileCommand { get; }
    public AsyncRelayCommand ExportProfileCommand { get; }
    public string ProfileSaveStatus { get => _profileSaveStatus; private set => Set(ref _profileSaveStatus, value); }
    public bool IsLive => Live?.HasWorkspace == true;
    public bool ShowDemoChat => IsChat && !IsLive;
    public bool ShowLiveChat => IsChat && IsLive;
    public string HeaderName => IsLive ? Live!.Dispatcher : Demo.Dispatcher;
    public string HeaderInitials => HeaderName[..1];
    public string HeaderFlight => IsLive ? Live!.FlightLabel : "BAW482 · London → Amsterdam";
    public string HeaderStage => IsLive ? Live!.StageLabel : Demo.StageLabel;
    public string HeaderGate => IsLive ? Live!.GateLabel : Demo.GateLabel;
    public string HeaderPersona => IsLive ? Live!.PersonaDescription : Demo.PersonaDescription;
    public bool ShowDemoRecovery => !IsLive && Demo.ShowRecovery;
    public string ConnectionText => IsLive ? Live!.ConnectionLabel : "DEMO FLIGHT · NO LIVE SERVICE CONTROL";
    public RelayCommand NavigateCommand { get; }
    public RelayCommand PreviewSoundCommand { get; }
    public IReadOnlyList<string> Themes { get; } = ["Dark", "Light"];
    public string PreferencesWarning { get; }
    public string SoundStatus { get => _soundStatus; private set => Set(ref _soundStatus, value); }
    public AppPage SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (!Set(ref _selectedPage, value)) return;
            foreach (string property in new[] { nameof(IsChat), nameof(IsProfiles), nameof(IsFlight), nameof(IsSettings), nameof(ShowDemoChat), nameof(ShowLiveChat) }) Raise(property);
        }
    }
    public bool IsChat => SelectedPage == AppPage.Chat;
    public bool IsProfiles => SelectedPage == AppPage.Profiles;
    public bool IsFlight => SelectedPage == AppPage.Flight;
    public bool IsSettings => SelectedPage == AppPage.Settings;
    public string Theme
    {
        get => _theme;
        set { if (value is "Dark" or "Light" && Set(ref _theme, value)) ThemeChanged?.Invoke(this, EventArgs.Empty); }
    }
    public bool Muted { get => _muted; set => Set(ref _muted, value); }
    public bool DoNotDisturb { get => _doNotDisturb; set => Set(ref _doNotDisturb, value); }
    public double Volume { get => _volume; set => Set(ref _volume, double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0); }
    public bool RealismEnabled { get => _realismEnabled; set => Set(ref _realismEnabled, value); }
    public int MaximumDelayMinutes { get => _maximumDelayMinutes; set => Set(ref _maximumDelayMinutes, Math.Clamp(value, 0, 10)); }
    public int DelayProbabilityPercent { get => _delayProbabilityPercent; set => Set(ref _delayProbabilityPercent, Math.Clamp(value, 0, 100)); }
    public bool PreviewDisconnected
    {
        get => Demo.IsInterrupted;
        set { Demo.PreviewConnectionLoss(value); Raise(); }
    }
    public UiPreferences CapturePreferences() => new(Theme, Muted, false, Volume, false, RealismEnabled, MaximumDelayMinutes)
    { ServiceSequences = Profiles.SavedSequences, DelayProbabilityPercent = DelayProbabilityPercent };
    private void PlayIncoming()
    {
        if (Muted || DoNotDisturb || Volume == 0)
        { SoundStatus = "Sound suppressed by mute, do not disturb, or zero volume."; return; }
        SoundStatus = "Playing the local notification preview.";
        _sound.Play(Volume / 100);
    }
}
