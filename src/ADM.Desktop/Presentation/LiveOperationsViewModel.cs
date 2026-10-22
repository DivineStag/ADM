using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Desktop.Services;
using ADM.Domain;
using ADM.Infrastructure;
using ADM.Integrations.Gsx;
using ADM.Integrations.Sim;
using Microsoft.Extensions.Logging;

namespace ADM.Desktop.Presentation;

/// <summary>Explicit live-operation controls. The demonstration remains isolated and never dispatches here.</summary>
public sealed class LiveOperationsViewModel : ObservableObject, IDisposable
{
    private readonly IOperationalClock _clock;
    private readonly IServicePlanResolver _resolver;
    private readonly IAirlineIdentityResolver _identity;
    private readonly IAirlineProfileRepository _profiles;
    private readonly IDispatchStore _store;
    private readonly IFlightPlanCache _cache;
    private readonly IFlightPlanImporter _importer;
    private readonly IConversationEngine _conversation;
    private readonly PersonaCatalogue _personas;
    private readonly JsonAircraftCapabilityCatalog _capabilities;
    private readonly ILogger<LiveOperationsViewModel>? _logger;
    private readonly IOperationalRealismEngine? _realismEngine;
    private readonly IAirportGateProfiles? _gateProfiles;
    private readonly IAssistantSetupStore _setupStore;
    private AssistantSetup _setup;
    private bool _automatic;
    private bool _commandFailed;
    private bool _reviewingRecovery;
    private bool _showManualEntry;
    private RecoveryStep _recovery = RecoveryStep.None;
    private readonly IGsxHelpLauncher? _gsxHelp;
    private readonly IInstallationRepair? _installationRepair;
    public event Action<string>? RepairRequested;
    public event Action<string, string, FlightOperationType>? FlightProfileChanged;
    private string? _profileContext;
    public event Action? FlightEntryRequested;
    private bool _setupReviewed;
    private string _startupMessage = "Connecting to your simulator…";
    private string? _lastGsxPlan;
    private DateTimeOffset _lastConnectionAttempt;
    private System.Windows.Threading.DispatcherTimer? _refreshTimer;
    public Func<RealismOptions> RealismPreferences { get; set; } = () => RealismOptions.Disabled;
    private GsxRemoteGateway? _gateway;
    private LiveDispatchCoordinator? _coordinator;
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private SimulatorSnapshot? _simulator;
    private Guid _sessionId;
    private FlightPlanSnapshot? _flight;
    private FlightPlanSnapshot? _nextFlight;
    private Guid? _visibleSession;
    private int _visibleOffset;
    private bool _liveWorkspace;
    private string _status = "Load a flight and connect. I'll ask in Chat before starting services.";
    private string _draft = "";
    private bool _connected;
    private string? _lastOperationalStatus;
    private bool _messagesInitialized;
    private bool _prepareWhenReady;
    public event Action? ImportantMessage;
    public LiveOperationsViewModel(IOperationalClock clock, IServicePlanResolver resolver, IAirlineIdentityResolver identity,
        IAirlineProfileRepository profiles, IDispatchStore store, IFlightPlanCache cache, IFlightPlanImporter importer,
        IConversationEngine conversation, PersonaCatalogue personas, JsonAircraftCapabilityCatalog capabilities, ILogger<LiveOperationsViewModel>? logger = null,
        IOperationalRealismEngine? realismEngine = null, IAirportGateProfiles? gateProfiles = null, IAssistantSetupStore? setupStore = null, IGsxHelpLauncher? gsxHelp = null, IInstallationRepair? installationRepair = null)
    {
        _gsxHelp = gsxHelp; _installationRepair = installationRepair;
        _clock = clock; _resolver = resolver; _identity = identity; _profiles = profiles; _store = store; _cache = cache;
        _importer = importer; _conversation = conversation; _personas = personas; _capabilities = capabilities;
        _logger = logger;
        _realismEngine = realismEngine; _gateProfiles = gateProfiles;
        _setupStore = setupStore ?? new MemoryAssistantSetupStore(); _setup = _setupStore.Load();
        RecoveryCommand = Command(RecoverAsync, () => _recovery.Action != RecoveryAction.None && !NeedsSetup);
        OpenGsxCommand = Command(() => { _gsxHelp?.Open(); return Task.CompletedTask; });
        ImportCommand = Command(ImportAsync);
        CachedCommand = Command(async () => StageFlight(await _cache.LoadAsync(CancellationToken.None) ?? throw new InvalidOperationException("No cached flight is available.")));
        ManualCommand = Command(async () =>
        {
            var candidate = new FlightPlanSnapshot(Callsign.Trim().ToUpperInvariant(), Origin.Trim().ToUpperInvariant(), Destination.Trim().ToUpperInvariant(),
            CanonicalAircraftIdentity.Create(AircraftType, AircraftFamily), _clock.UtcNow); FlightPlanValidation.Validate(candidate, _clock.UtcNow);
            if (_gateway?.Snapshot.AircraftIcaoType is { } observedType && observedType != candidate.Aircraft.IcaoType) throw new InvalidOperationException("The entered aircraft type differs from GSX. Check the ICAO type.");
            await _cache.SaveAsync(candidate, CancellationToken.None); StageFlight(candidate); FlightConfirmed = true; _prepareWhenReady = true; Raise(nameof(FlightConfirmed)); Refresh();
        });
        ConnectCommand = Command(ConnectAsync);
        StartCommand = Command(StartAsync, () => _connected && _coordinator?.Checkpoint is null);
        ReconcileCommand = Command(async () => { await Required().ReconcileAsync(Simulator(), ReviewedCurrentServices, CancellationToken.None); Refresh(); }, () => _coordinator?.Checkpoint is not null);
        GateCommand = Command(async () => { await Required().ConfirmGateAsync(Simulator(), CancellationToken.None); Refresh(); }, () => _coordinator?.Checkpoint is not null);
        SendCommand = Command(async () => { await SendMessageAsync(Draft); Draft = ""; Refresh(); }, () => CanChat && !string.IsNullOrWhiteSpace(Draft));
        QuickReplyCommand = new(value => { Draft = value as string ?? ""; SendCommand.Execute(null); }, _ => CanChat);
        EndCommand = Command(async () => { await Required().EndAsync(CancellationToken.None); await StopAsync(); await CreateCoordinatorAsync(); ClearFlight(); if (_automatic) await ConnectAsync(); Refresh(); }, () => _coordinator?.Checkpoint is not null);
        ClearChatCommand = Command(async () => { await Required().ClearChatAsync(CancellationToken.None); Refresh(); }, () => HasOperation);
        PrepareNextCommand = Command(PrepareNextAsync, () => _nextFlight is not null && NextSectorConfirmed);
        EndDutyCommand = Command(async () => { _automatic = false; await Required().EndDutyAsync(CancellationToken.None); await StopAsync(); await CreateCoordinatorAsync(); _nextFlight = null; ClearFlight(); Refresh(); }, () => _coordinator?.HasDuty == true);
        HistoryCommand = Command(LoadHistoryAsync);
        CompleteSetupCommand = Command(async () =>
        {
            string key = SetupKey ?? throw new InvalidOperationException("Waiting for the aircraft in GSX.");
            var aircraft = _setup.Aircraft.Add(key);
            if (_simulator?.AircraftTitle is { Length: > 0 } title) aircraft = aircraft.Add("title:" + title);
            var configured = _setup with { Aircraft = aircraft }; _setupStore.Save(configured); _setup = configured;
            SetupReviewed = false; await Task.CompletedTask; Refresh();
        }, () => SetupReviewed && SetupKey is not null);
        ReviewSetupCommand = Command(async () =>
        {
            var aircraft = _setup.Aircraft;
            if (SetupKey is { } key) aircraft = aircraft.Remove(key);
            if (_simulator?.AircraftTitle is { Length: > 0 } title) aircraft = aircraft.Remove("title:" + title);
            var revised = _setup with { Aircraft = aircraft }; _setupStore.Save(revised); _setup = revised;
            if (_coordinator?.IsArmed == true) await _coordinator.PauseAsync("Aircraft setup is being reviewed. Complete it, then resume handling in Chat.", CancellationToken.None);
            await Task.CompletedTask; Refresh();
        });
    }
    public AsyncRelayCommand RecoveryCommand { get; }
    public AsyncRelayCommand OpenGsxCommand { get; }
    public bool ShowManualEntry { get => _showManualEntry; set => Set(ref _showManualEntry, value); }
    public bool HasRecovery => _recovery.Action != RecoveryAction.None;
    public string RecoveryTitle => _recovery.Title;
    public string RecoveryDetail => _reviewingRecovery && _recovery.Action == RecoveryAction.Resume ? "Check that the current aircraft and GSX services belong to this flight. The button below confirms that review; it doesn't replay uncertain requests." : _recovery.Detail;
    public string RecoveryButton => _reviewingRecovery && _recovery.Action == RecoveryAction.Resume ? "States checked — resume" : _recovery.Button;
    public bool HasGsxShortcut => _recovery.Action is RecoveryAction.Resume or RecoveryAction.RestartService or RecoveryAction.EnterFlight;
    public SupportReport SupportSnapshot() => new(typeof(LiveOperationsViewModel).Assembly.GetName().Version?.ToString() ?? "unknown", _clock.UtcNow,
        _simulator?.Connection.ToString() ?? "Offline", _gateway?.Connection.ToString() ?? "Offline", StageLabel,
        _gateway?.Snapshot.MenuOpen == true, _gateway?.Snapshot.AwaitingInput == true, _recovery.Action.ToString(),
        _coordinator?.Checkpoint?.State.Session.Services.Where(pair => pair.Key != ServiceKind.Deboarding).ToDictionary(pair => pair.Key, pair => pair.Value.State) ?? []);
    private async Task RecoverAsync()
    {
        var action = _recovery;
        switch (action.Action)
        {
            case RecoveryAction.Reconnect:
                if (_coordinator?.IsArmed == true) await _coordinator.PauseAsync("Rechecking connections. Existing requests will not be replayed.", CancellationToken.None);
                await StopAsync(); await CreateCoordinatorAsync(); await ConnectAsync(); break;
            case RecoveryAction.EnterFlight:
                ShowManualEntry = true; FlightEntryRequested?.Invoke(); break;
            case RecoveryAction.OpenGsx: _gsxHelp?.Open(); break;
            case RecoveryAction.Resume:
                if (!_reviewingRecovery) { _reviewingRecovery = true; return; }
                await Required().ReconcileAsync(Simulator(), true, CancellationToken.None); _reviewingRecovery = false; break;
            case RecoveryAction.RestartService:
                await SendMessageAsync("Restart " + (action.Service == ServiceKind.Refuelling ? "fuel" : action.Service!.Value.ToString().ToLowerInvariant())); break;
            case RecoveryAction.Prepare: await StartAsync(); break;
            case RecoveryAction.RepairInstall:
                if (_installationRepair?.FindInstaller() is { } installer) RepairRequested?.Invoke(installer); break;
        }
    }
    public AsyncRelayCommand CompleteSetupCommand { get; }
    public AsyncRelayCommand ReviewSetupCommand { get; }
    private string? SetupKey => _gateway?.Snapshot.AircraftSetupKey ?? (_gateway?.Connection == ConnectionState.Connected && _simulator?.AircraftTitle is { Length: > 0 } title ? "title:" + title : null);
    public bool SetupReviewed { get => _setupReviewed; set { if (Set(ref _setupReviewed, value)) CompleteSetupCommand.Refresh(); } }
    private bool AircraftConfigured => SetupKey is { } key && (_setup.Aircraft.Contains(key) || _simulator?.AircraftTitle is { Length: > 0 } title && _setup.Aircraft.Contains("title:" + title));
    public bool NeedsSetup => _automatic && SetupKey is not null && !AircraftConfigured;
    public bool ShowWelcome => !HasOperation && !NeedsSetup && !HasRecovery;
    public string WelcomeMessage => _startupMessage;
    public bool AutoConnect
    {
        get => _setup.AutoConnect;
        set
        {
            try { var revised = _setup with { AutoConnect = value }; _setupStore.Save(revised); _setup = revised; Raise(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Status = "Could not save startup preference: " + exception.Message; }
        }
    }
    public string LoadLabel => _flight is { Source: FlightPlanSource.Gsx } flight ? $"GSX flight plan · {flight.PlannedPassengers?.ToString() ?? "—"} planned passengers · ramp fuel {flight.PlannedRampFuelKg?.ToString("N0") ?? "—"} kg" : _flight is null ? "Waiting for a flight plan" : "Flight details entered by the pilot";
    public async Task StartAutomaticAsync()
    {
        _automatic = true; _liveWorkspace = true; _lastConnectionAttempt = _clock.UtcNow; Refresh();
        if (System.Windows.Application.Current is { } app && _refreshTimer is null)
            _refreshTimer = new(TimeSpan.FromSeconds(1), System.Windows.Threading.DispatcherPriority.Background, (_, _) => Refresh(), app.Dispatcher);
        if (AutoConnect) await ConnectAsync();
        else { _startupMessage = "Automatic connection is off. Connect from Flight when you're ready."; Refresh(); }
    }
    private bool _nextSectorConfirmed;
    public bool NextSectorConfirmed { get => _nextSectorConfirmed; set { if (Set(ref _nextSectorConfirmed, value)) PrepareNextCommand.Refresh(); } }
    public bool HasWorkspace => _liveWorkspace || HasOperation || _coordinator?.HasDuty == true;
    public string NextFlightLabel => _nextFlight is null ? "Load or enter the next leg when ready." : $"Next: {_nextFlight.Callsign} · {_nextFlight.Origin} → {_nextFlight.Destination}";
    public string DutyLabel => _coordinator?.Duty is { State: not DutyState.Ended } duty ? $"Duty · {duty.State} · {duty.Sectors.Length} sector(s)" : "No active duty";
    public string ArrivalLabel => _coordinator?.Duty?.Arrival is { } arrival ? $"{arrival.Reason} · {arrival.Airport ?? "airport unknown"} · {arrival.Stand ?? "stand unknown"}. Confirm the actual location before the next sector." : "Arrival monitoring continues after handover. Airport and stand checks never authorize services.";
    public ObservableCollection<string> SectorHistory { get; } = [];
    public ObservableCollection<string> HistoryMessages { get; } = [];
    public AsyncRelayCommand ClearChatCommand { get; }
    public AsyncRelayCommand PrepareNextCommand { get; }
    public AsyncRelayCommand EndDutyCommand { get; }
    public AsyncRelayCommand HistoryCommand { get; }
    public string PilotId { get; set; } = "";
    public string Callsign { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string AircraftType { get; set; } = "";
    public string AircraftFamily { get; set; } = "";
    public bool AutomationDisabled { get; set; }
    public bool IntegrationReviewed { get; set; }
    public bool FlightConfirmed { get; set; }
    public bool ReviewedCurrentServices { get; set; }
    private string _selectedAirline = "Automatic";
    public string SelectedAirline { get => _selectedAirline; set { if (Set(ref _selectedAirline, value) && !HasOperation) PopulateFlight(); } }
    public IReadOnlyList<string> Airlines => new[] { "Automatic", "GENERIC", "RYR", "BAW", "UAL" }.Concat(SavedProfiles.Keys.Order()).Distinct().ToArray();
    public FlightOperationType Operation { get; set; } = FlightOperationType.FirstDeparture;
    public IReadOnlyList<FlightOperationType> Operations { get; } = Enum.GetValues<FlightOperationType>();
    public IReadOnlyList<OperationOption> OperationChoices { get; } = [new(FlightOperationType.FirstDeparture, "First departure"), new(FlightOperationType.Turnaround, "Turnaround")];
    public OperationOption SelectedOperation { get => OperationChoices.First(option => option.Value == Operation); set { Operation = value.Value; Raise(); if (!HasOperation) PopulateFlight(); } }
    public string Draft { get => _draft; set { if (Set(ref _draft, value)) SendCommand.Refresh(); } }
    public string Status { get => _status; private set { if (Set(ref _status, value)) Raise(nameof(ChatStatus)); } }
    public string ChatStatus => !HasOperation && Status == _coordinator?.Status ? "" : Status;
    public bool CanChat => HasOperation || _nextFlight is not null;
    public string ServiceDetails => _coordinator?.Checkpoint is { } checkpoint ? OperationalReducer.DelayDescription(checkpoint.State) : "No handling operation prepared.";
    public string RealismDetails => _coordinator?.Checkpoint?.State.Realism?.Incident is { Status: DisruptionStatus.Active } incident
        ? $"Simulated earliest boarding: {incident.ExpectedResolution:HH:mm} UTC (estimate). Service and gate checks still apply. Skip the simulated delay in Chat if needed." : "";
    public string FlightLabel => _flight is null ? "No flight loaded" : $"{_flight.Callsign} · {_flight.Origin} → {_flight.Destination} · {_flight.Aircraft.IcaoType}";
    public string ProfileLabel { get; private set; } = "Airline will be resolved from the validated ATC callsign or your explicit selection.";
    public string Compatibility { get; private set; } = "Basic GSX services only. Cancellation and equipment actions unsupported. No validated real-aircraft concurrency. Any2GSX ownership is pilot-attested, not automatically verified.";
    public string ConnectionLabel
    {
        get
        {
            if (!_connected) return "Live connections stopped";
            bool sim = _simulator?.Connection == ConnectionState.Connected && _clock.UtcNow - _simulator.ObservedAt < TimeSpan.FromSeconds(5);
            bool gsx = _gateway?.Connection == ConnectionState.Connected;
            return sim && gsx ? "GSX and MSFS connected" : sim ? "Waiting for GSX" : gsx ? "Waiting for MSFS" : "Waiting for MSFS and GSX";
        }
    }
    public string Dispatcher => _coordinator?.Checkpoint?.State.Session.Dispatcher.Persona.Name ?? "ADM";
    public string PersonaDescription => _coordinator?.Checkpoint?.State.Session.Dispatcher.Persona.Tone switch
    { "WarmProfessional" => "Warm professional", "FormalConcise" => "Formal", _ => "Friendly" };
    public string StageLabel => _coordinator?.Checkpoint?.State.Session.Stage switch
    {
        WorkflowStage.Planning => "Awaiting service-start approval",
        WorkflowStage.AwaitingBoardingApproval => "Awaiting boarding approval",
        WorkflowStage.Boarding => "Boarding",
        WorkflowStage.AwaitingClosureApproval => "Awaiting closure approval",
        WorkflowStage.ClosingAndReleasing => "Doors and equipment checks",
        WorkflowStage.Completed => "Ground handling complete",
        WorkflowStage.NeedsAttention => "Pilot review required",
        _ => _coordinator?.Checkpoint is not null ? _coordinator.IsArmed ? "Ground services" : "Review required" : _coordinator?.Duty?.State switch
        {
            DutyState.EnrouteObserved => "In flight",
            DutyState.ArrivalCandidate or DutyState.Paused => "Arrival / location review",
            DutyState.ReleasedForFlight => "Released for flight",
            DutyState.Ended => "Awaiting flight",
            _ => "Awaiting next flight"
        }
    };
    public string GateLabel => _coordinator?.Checkpoint?.State.Session.Gate switch
    {
        { State: GateReadinessState.Ready, Provenance: GateProvenance.Simulation } => "Gate ready · simulated",
        { State: GateReadinessState.Ready, Provenance: GateProvenance.VerifiedExternal } => "Gate ready · verified",
        { State: GateReadinessState.Ready } => "Gate ready · pilot confirmed",
        { State: GateReadinessState.Held, Provenance: GateProvenance.Simulation } => "Gate held · simulated",
        { State: GateReadinessState.Preparing, Provenance: GateProvenance.Simulation } => "Gate preparing · simulated",
        _ => _coordinator?.Checkpoint is null ? "No active gate" : "Gate check required"
    };
    public void ReportError(string message) { _commandFailed = true; Status = message; Refresh(); }
    public bool HasOperation => _coordinator?.Checkpoint is not null;
    public bool HandlingActive => _coordinator?.Checkpoint is { State.Session.Stage: not WorkflowStage.Completed };
    public ObservableCollection<string> ServiceStatus { get; } = [];
    public ObservableCollection<DemoMessage> Messages { get; } = [];
    public ObservableCollection<string> QuickReplies { get; } = [];
    public ObservableCollection<string> ReleaseStatus { get; } = [];
    public ImmutableDictionary<string, AirlineProfile> SavedProfiles { get; private set; } = ImmutableDictionary<string, AirlineProfile>.Empty;
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand CachedCommand { get; }
    public AsyncRelayCommand ManualCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand ReconcileCommand { get; }
    public AsyncRelayCommand GateCommand { get; }
    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand QuickReplyCommand { get; }
    public AsyncRelayCommand EndCommand { get; }
    public async Task InitializeAsync(bool restorePreviousSession = false)
    {
        foreach (string code in new[] { "GENERIC", "BAW", "RYR", "UAL" })
            if (await _profiles.FindAsync(code, CancellationToken.None) is { } profile) SavedProfiles = SavedProfiles.SetItem(code, profile);
        foreach (var profile in await _profiles.ListAsync(CancellationToken.None)) SavedProfiles = SavedProfiles.SetItem(profile.Icao, profile);
        foreach (string key in SavedProfiles.Keys) if (_identity is IUserAirlineRegistry registry) registry.RegisterProfileDesignator(key);
        Raise(nameof(Airlines));
        // Cached OFPs are available only through the explicit Use cached flight action.
        _flight = null;
        if (!restorePreviousSession) await FreshLaunch.ArchivePreviousAsync(_store, CancellationToken.None);
        await CreateCoordinatorAsync();
        if (restorePreviousSession && _coordinator!.Flight is { } restored) _flight = restored;
        _liveWorkspace = HasOperation || _coordinator!.HasDuty;
        await LoadHistoryAsync();
        PopulateFlight(); Refresh();
    }
    private async Task CreateCoordinatorAsync()
    {
        var saved = await _store.LoadActiveAsync(CancellationToken.None);
        _sessionId = saved?.Checkpoint.State.Session.Id ?? Guid.NewGuid();
        _gateway = new(_sessionId, diagnostic: message => _logger?.LogInformation("{GsxDiagnostic}", message));
        _coordinator = new(_gateway, _gateway, _store, _conversation, _clock, _sessionId, () => _simulator,
            message => _logger?.LogInformation("{HandlingDiagnostic}", message), _realismEngine);
        await _coordinator.RestoreAsync(CancellationToken.None);
    }
    private void StageFlight(FlightPlanSnapshot flight)
    {
        FlightConfirmed = false; ReviewedCurrentServices = false; SelectedAirline = "Automatic";
        if (HasOperation || _coordinator?.HasDuty == true)
        {
            _nextFlight = flight; NextSectorConfirmed = false;
            Status = "Next flight staged. Confirm the arrival location and next sector in Flight; current handling keeps its saved plan.";
            Raise(nameof(NextFlightLabel)); PrepareNextCommand.Refresh();
        }
        else { _flight = flight; PopulateFlight(); }
        Raise(nameof(FlightConfirmed)); Raise(nameof(ReviewedCurrentServices)); Raise(nameof(SelectedAirline));
    }
    private void ClearFlight()
    {
        _profileContext = null; _flight = null; Callsign = ""; Origin = ""; Destination = ""; AircraftType = ""; AircraftFamily = "";
        FlightConfirmed = false; ReviewedCurrentServices = false; SelectedAirline = "Automatic"; Draft = "";
        ProfileLabel = "Load the next flight when ready.";
        foreach (string name in new[] { nameof(Callsign), nameof(Origin), nameof(Destination), nameof(AircraftType), nameof(AircraftFamily), nameof(FlightConfirmed), nameof(ReviewedCurrentServices), nameof(SelectedAirline), nameof(FlightLabel), nameof(ProfileLabel) }) Raise(name);
    }
    private async Task LoadHistoryAsync()
    {
        HistoryMessages.Clear();
        if (_store is not IDutyStore duties || _coordinator?.Duty is not { } duty) return;
        foreach (var archived in await duties.HistoryAsync(duty.Id, CancellationToken.None))
        {
            HistoryMessages.Add($"{archived.Flight.Callsign} · {archived.Flight.Origin} → {archived.Flight.Destination} · read-only archive");
            foreach (var message in archived.Checkpoint.Messages) HistoryMessages.Add($"{message.SentAt:HH:mm} · {message.Sender}: {message.Text}");
        }
    }
    private async Task PrepareNextAsync()
    {
        var flight = _nextFlight ?? throw new InvalidOperationException("Load or enter the next flight first.");
        if (!NextSectorConfirmed) throw new InvalidOperationException("Confirm the actual airport and next sector first.");
        if (_coordinator?.Checkpoint is { State.Session.Stage: not WorkflowStage.Completed })
            throw new InvalidOperationException("Handling is still active. Finish it or explicitly archive the operation before starting another sector.");
        LiveDispatchCoordinator.ValidateEnvironment(Simulator(), _gateway!.Snapshot, flight, null);
        if (Simulator().AirportIcao is { Length: > 0 } airport && airport != flight.Origin)
            throw new InvalidOperationException("The next flight origin does not match the observed airport.");
        await Required().EndAsync(CancellationToken.None);
        await LoadHistoryAsync();
        await StopAsync(); await CreateCoordinatorAsync();
        _flight = flight; _nextFlight = null; Operation = FlightOperationType.Turnaround; PopulateFlight();
        Raise(nameof(SelectedOperation)); Raise(nameof(NextFlightLabel));
        await ConnectAsync(); // New gateway/session identity; the existing start question still gates every request.
        Status = "Next sector selected. Check the route and aircraft; I'll ask in Chat before services start.";
        Refresh();
    }
    private async Task ImportAsync()
    {
        var result = await _importer.ImportLatestAsync(PilotId, CancellationToken.None);
        if (result.Plan is null) throw new InvalidOperationException(result.Error);
        FlightPlanValidation.Validate(result.Plan, _clock.UtcNow);
        await _cache.SaveAsync(result.Plan, CancellationToken.None); StageFlight(result.Plan);
        Status = _nextFlight is null ? "Flight imported. Check the route and aircraft before starting." : "Next flight staged. Current handling is unchanged; confirm the next sector in Flight.";
    }
    private void PopulateFlight()
    {
        if (_flight is not null)
        {
            Callsign = _flight.Callsign; Origin = _flight.Origin; Destination = _flight.Destination; AircraftType = _flight.Aircraft.IcaoType ?? ""; AircraftFamily = _flight.Aircraft.Family ?? "";
            if (!HasOperation)
            {
                var resolution = _identity.Resolve(new(_flight.Callsign, null, null, false), SelectedAirline == "Automatic" ? null : new(SelectedAirline));
                ProfileLabel = resolution.Icao == "GENERIC" ? "GENERIC · no validated airline designator in the ATC callsign" : $"{resolution.Icao} · " + (_flight.Source == FlightPlanSource.Gsx ? "GSX flight plan" : "Pilot-entered ATC callsign");
                string context = $"{PlanKey(_flight)}/{resolution.Icao}/{Operation}";
                if (_profileContext != context)
                {
                    _profileContext = context;
                    FlightProfileChanged?.Invoke(resolution.Icao, _flight.Aircraft.IcaoType ?? "", Operation);
                }
            }
        }
        foreach (string name in new[] { nameof(Callsign), nameof(Origin), nameof(Destination), nameof(AircraftType), nameof(AircraftFamily), nameof(FlightLabel), nameof(LoadLabel), nameof(ProfileLabel) }) Raise(name);
    }
    private static string PlanKey(FlightPlanSnapshot plan) => $"{plan.Callsign}/{plan.Origin}/{plan.Destination}/{plan.Aircraft.IcaoType}/" +
        (plan.ScheduledDepartureUtc is { } schedule ? schedule.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture) : plan.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    private void AdvanceAutomaticPreparation()
    {
        var gsx = _gateway?.Snapshot;
        if (!HasOperation && _flight is null)
        {
            if (string.IsNullOrWhiteSpace(AircraftType) && gsx?.AircraftIcaoType is { } type) { AircraftType = type; Raise(nameof(AircraftType)); }
            if (string.IsNullOrWhiteSpace(Origin) && gsx?.AirportIcao is { } airport) { Origin = airport; Raise(nameof(Origin)); }
        }
        if (gsx?.Connection == ConnectionState.Connected)
        {
            if (gsx.FlightPlan is { } incoming) ReceiveGsxPlan(incoming);
            else if (!HasOperation && _flight?.Source == FlightPlanSource.Gsx) { ClearFlight(); _lastGsxPlan = null; }
        }
        bool configured = AircraftConfigured;
        AutomationDisabled = configured; IntegrationReviewed = configured;
        if (_flight?.Source == FlightPlanSource.Gsx) FlightConfirmed = AutomaticPreparation.Check(_flight, _simulator, gsx, configured, _clock.UtcNow).Ready;
        if (!HasOperation)
        {
            _startupMessage = !AutoConnect && !_connected ? "Automatic connection is off. Connect from Flight when you're ready." :
                _nextFlight is not null ? $"Next flight: {_nextFlight.Callsign}, {_nextFlight.Origin} to {_nextFlight.Destination}. Confirm we're at its origin to start the next sector." :
                _coordinator?.HasDuty == true ? _coordinator.Duty?.State == DutyState.EnrouteObserved ? "In flight. I'll keep watching for our arrival." : "Handling finished. Waiting for the next flight plan in GSX." :
                AutomaticPreparation.Check(_flight, _simulator, gsx, configured, _clock.UtcNow).Message;
        }
    }
    // Refresh planned figures even when the OFP identity is unchanged. The coordinator's applied plan stays immutable.
    public void ReceiveGsxPlan(FlightPlanSnapshot incoming)
    {
        if (incoming.Source != FlightPlanSource.Gsx) throw new ArgumentException("Expected a GSX flight plan.");
        string key = PlanKey(incoming);
        if (_flight is { Source: FlightPlanSource.Gsx } current && PlanKey(current) == key)
        {
            if (current != incoming) { _flight = incoming; PopulateFlight(); }
            _lastGsxPlan = key; return;
        }
        if (_nextFlight is { Source: FlightPlanSource.Gsx } next && PlanKey(next) == key)
        { _nextFlight = incoming; Raise(nameof(NextFlightLabel)); return; }
        if (key == _lastGsxPlan) return;
        _lastGsxPlan = key;
        bool previousSector = _coordinator?.HasDuty == true && _coordinator.Duty?.Sectors.LastOrDefault() is { } previous && PlanKey(previous.Flight) == key;
        if (previousSector) return;
        if (HasOperation || _coordinator?.HasDuty == true) { _nextFlight = incoming; NextSectorConfirmed = false; Raise(nameof(NextFlightLabel)); }
        else { _flight = incoming; PopulateFlight(); _prepareWhenReady = true; }
    }
    private async Task SendMessageAsync(string text)
    {
        string command = text.Trim().TrimEnd('.', '!').ToLowerInvariant();
        if (command == "start next sector")
        {
            if (_nextFlight is null) throw new InvalidOperationException("Waiting for the next flight plan.");
            if (NeedsSetup) throw new InvalidOperationException("Complete this aircraft's one-time setup first.");
            NextSectorConfirmed = true; await PrepareNextAsync(); return;
        }
        if (command == "resume handling")
        {
            if (NeedsSetup) throw new InvalidOperationException("Complete this aircraft's one-time setup first.");
            await Required().ReconcileAsync(Simulator(), true, CancellationToken.None); Refresh(); return;
        }
        if (command.StartsWith("restart ", StringComparison.Ordinal) && _coordinator?.Checkpoint is not null && !_coordinator.IsArmed)
            await Required().ReconcileAsync(Simulator(), true, CancellationToken.None);
        await Required().SendAsync(text, Simulator(), CancellationToken.None);
    }
    private async Task ConnectAsync()
    {
        _liveWorkspace = true; _lastConnectionAttempt = _clock.UtcNow;
        if (_worker is { IsCompleted: false }) return;
        if (_worker is not null) { await StopAsync(); await CreateCoordinatorAsync(); }
        if (_coordinator is null) await CreateCoordinatorAsync();
        _stop = new(); _gateway!.Start(); _connected = true;
        _prepareWhenReady = !HasOperation;
        var ct = _stop.Token;
        _worker = Task.Run(async () =>
        {
            var telemetry = new SimConnectTelemetry(diagnostic: message => _logger?.LogInformation("{SimulatorDiagnostic}", message));
            var reader = Task.Run(async () =>
            {
                await foreach (var snapshot in telemetry.WatchAsync(ct).ConfigureAwait(false)) _simulator = snapshot;
            }, ct);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (reader.IsFaulted) await reader.ConfigureAwait(false);
                    if (_simulator is { } sim && _coordinator is { } coordinator) await coordinator.ObserveAsync(sim, ct).ConfigureAwait(false);
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Refresh());
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "Live handling stopped; no command will be replayed."); _connected = false;
                try { await Required().PauseAsync("Live worker stopped; reconcile before continuing.", CancellationToken.None).ConfigureAwait(false); }
                catch (Exception saveError) { _logger?.LogError(saveError, "Could not persist the stopped worker checkpoint."); }
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { Status = "Live handling stopped: " + exception.Message + " Reconnect, review and reconcile before continuing."; Raise(nameof(ConnectionLabel)); });
            }
            finally { await _stop!.CancelAsync().ConfigureAwait(false); try { await reader.ConfigureAwait(false); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { } catch (Exception exception) { _logger?.LogError(exception, "Simulator reader stopped."); } }
        }, ct);
        Refresh();
    }
    private async Task StartAsync()
    {
        var flight = _flight ?? throw new InvalidOperationException("Load or enter a flight first.");
        var selection = SelectedAirline == "Automatic" ? null : new UserAirlineOverride(SelectedAirline);
        var identity = _identity.Resolve(new(flight.Callsign, null, null, false), selection);
        var generic = await _profiles.FindAsync("GENERIC", CancellationToken.None) ?? SampleSessionFactory.GenericProfile();
        var airline = identity.Icao == "GENERIC" ? null : await _profiles.FindAsync(identity.Icao, CancellationToken.None);
        // No saved airline profile: generic service preferences still retain the resolved carrier identity.
        bool genericOnly = airline is null;
        airline ??= identity.Icao == "GENERIC" ? null : new AirlineProfile(1, identity.Icao, identity.Icao, 1,
            ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty, ImmutableDictionary<string, AircraftProfileOverride>.Empty, ImmutableDictionary<string, AircraftProfileOverride>.Empty);
        var aircraft = CanonicalAircraftIdentity.Create(AircraftType.Trim().ToUpperInvariant(), AircraftFamily);
        var plan = _resolver.Resolve(generic, airline, aircraft, Operation, new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty));
        var capabilities = _capabilities.Resolve(aircraft.IcaoType!);
        ProfileLabel = $"{identity.Icao} · " + (identity.Source switch { AirlineSource.SimBriefAtcCallsign => "ATC callsign", AirlineSource.FlightOverride => "Pilot override", _ => "Generic fallback" }) +
            $" · {(genericOnly ? "generic defaults" : "airline preferences")} revision {(genericOnly ? generic.Revision : plan.ProfileRevision)}";
        Compatibility = capabilities.EvidenceReference + " Fuel and catering are requested together when the saved phases and GSX availability allow. Cancellation and release unsupported; competing automation is pilot-attested only.";
        var realism = generic.Realism?.Apply(RealismPreferences()) ?? RealismPreferences();
        realism = airline?.Realism?.Apply(realism) ?? realism;
        await Required().StartAsync(flight, new(AutomationDisabled, IntegrationReviewed, FlightConfirmed, Origin.Trim().ToUpperInvariant(), aircraft.IcaoType!, Operation,
            selection, ReviewedCurrentServices)
        { SectorTransitionConfirmed = NextSectorConfirmed, Realism = realism, GateProfile = _gateProfiles?.Resolve(flight.Origin) ?? AirportGateProfile.Generic }, plan, _personas.Select(_sessionId, _coordinator?.Duty?.Sectors.LastOrDefault()?.Dispatcher.Persona.Id), capabilities, Simulator(), CancellationToken.None);
        NextSectorConfirmed = false;
        _visibleSession = _coordinator!.Checkpoint!.State.Session.Id;
        _visibleOffset = _coordinator.Checkpoint.VisibleMessageOffset;
        Messages.Clear();
        _messagesInitialized = true; // A new live question should ping; restored history remains silent.
        Refresh();
    }
    public async Task SaveProfileAsync(AirlineProfile edited)
    {
        await _profiles.SaveAsync(edited, CancellationToken.None);
        SavedProfiles = SavedProfiles.SetItem(edited.Icao, edited);
        if (_identity is IUserAirlineRegistry registry) registry.RegisterProfileDesignator(edited.Icao);
        Raise(nameof(Airlines));
        Status = $"{edited.Icao} profile revision {edited.Revision} saved for future operations.";
    }
    private void Refresh()
    {
        if (_automatic) AdvanceAutomaticPreparation();
        var snapshot = _gateway?.Snapshot;
        bool ready = _connected && snapshot?.Connection == ConnectionState.Connected && _simulator is { Connection: ConnectionState.Connected } sim && _clock.UtcNow - sim.ObservedAt < TimeSpan.FromSeconds(5);
        var checkpointState = _coordinator?.Checkpoint?.State;
        ServiceKind? restartableService = checkpointState?.Session.Services.Values.FirstOrDefault(service => service.Kind != ServiceKind.Deboarding && service.State is ServiceState.Unknown or ServiceState.Failed && snapshot?.Services.GetValueOrDefault(service.Kind)?.CanTrigger == true)?.Kind;
        _recovery = _automatic || HasOperation || _commandFailed ? RecoveryGuide.Choose(new(ready, HasOperation, _flight is not null, _coordinator?.IsArmed == true,
            _commandFailed || checkpointState?.Session.Services.Values.Any(service => service.State is ServiceState.Unknown or ServiceState.Failed) == true,
            snapshot?.AwaitingInput == true, snapshot?.MenuOpen == true, restartableService, NeedsSetup, checkpointState?.Session.Stage == WorkflowStage.Completed, _installationRepair?.RuntimeAvailable ?? true)) : RecoveryStep.None;
        if (_recovery.Action != RecoveryAction.Resume) _reviewingRecovery = false;
        if (_coordinator is not null && _lastOperationalStatus != _coordinator.Status) { Status = _coordinator.Status; _lastOperationalStatus = _coordinator.Status; }
        if (_coordinator?.Checkpoint is { } checkpoint)
        {
            ServiceStatus.Clear();
            foreach (var kind in ServiceSequence.Ordered(checkpoint.State.Session.Plan).Where(kind => kind != ServiceKind.Deboarding && checkpoint.State.Session.Services[kind].State != ServiceState.NotRequired))
                ServiceStatus.Add($"{kind} · " + (checkpoint.State.Session.Services[kind].State switch
                {
                    ServiceState.RequestPending => "Request queued",
                    ServiceState.Requested => "Requested in GSX",
                    ServiceState.Active => "In progress · GSX",
                    ServiceState.Completed => checkpoint.State.Session.Services[kind].CompletionProvenance == CompletionProvenance.PilotAttested ? "Complete · pilot confirmed" : "Complete · verified",
                    ServiceState.CycleEnded => "Returned idle · GSX cycle ended",
                    ServiceState.NotRequired => "Not required",
                    ServiceState.CancelPending => "Cancellation pending",
                    ServiceState.Unknown => "Unknown · review required",
                    ServiceState.Failed => "Needs attention",
                    ServiceState.Skipped => "Skipped by pilot",
                    ServiceState.Cancelled => "Cancelled",
                    _ => "Planned"
                }));
            if (_visibleSession != checkpoint.State.Session.Id || _visibleOffset != checkpoint.VisibleMessageOffset)
            { Messages.Clear(); _visibleSession = checkpoint.State.Session.Id; _visibleOffset = checkpoint.VisibleMessageOffset; _messagesInitialized = false; }
            bool notify = false;
            while (Messages.Count + _visibleOffset < checkpoint.Messages.Length)
            {
                var message = checkpoint.Messages[Messages.Count + _visibleOffset]; Messages.Add(new(message.Sender, message.Text, message.SentAt, message.Sender == "You") { IsSystem = message.IsSystem });
                if (ConversationPresentation.ShouldNotify(message)) notify = true;
            }
            if (_messagesInitialized && notify) ImportantMessage?.Invoke();
            _messagesInitialized = true;
            var context = new OperationalContext(checkpoint.State.Session, checkpoint.State.Connection, checkpoint.State.Question);
            var restartable = checkpoint.State.Session.Services.Values.FirstOrDefault(service => service.Kind != ServiceKind.Deboarding && service.State == ServiceState.Unknown && _gateway?.Snapshot.Services.GetValueOrDefault(service.Kind)?.CanTrigger == true);
            var replies = _automatic && restartable is not null ? new[] { "Restart " + (restartable.Kind == ServiceKind.Refuelling ? "fuel" : restartable.Kind.ToString().ToLowerInvariant()), "What's the delay?" } :
                _automatic && !_coordinator.IsArmed && checkpoint.State.Session.Stage != WorkflowStage.Completed ? new[] { "Resume handling" } :
                checkpoint.State.Connection == ConnectionState.Connected ? LocalConversationEngine.QuickReplies(context).ToArray() : ["What's the delay?"];
            if (_nextFlight is not null && checkpoint.State.Session.Stage == WorkflowStage.Completed) replies = ["Start next sector", .. replies];
            if (!QuickReplies.SequenceEqual(replies)) { QuickReplies.Clear(); foreach (var reply in replies) QuickReplies.Add(reply); }
            ReleaseStatus.Clear();
            if (checkpoint.State.Session.Stage is WorkflowStage.ClosingAndReleasing or WorkflowStage.Completed)
                foreach (var (action, release) in checkpoint.State.Releases.OrderBy(pair => pair.Key))
                    ReleaseStatus.Add((action switch { ReleaseAction.Doors => "Doors closed", ReleaseAction.JetwayOrStairs => "Jetway / stairs clear", _ => "Ground equipment disconnected" }) +
                        (release.IsSatisfied ? release.Provenance == CompletionProvenance.PilotAttested ? " · pilot confirmed" : " · verified" : " · manual confirmation pending"));
        }
        else
        {
            ServiceStatus.Clear(); Messages.Clear(); QuickReplies.Clear(); ReleaseStatus.Clear(); _messagesInitialized = false;
            if (_nextFlight is not null) QuickReplies.Add("Start next sector");
            if (_flight is not null && _coordinator?.Duty is { DepartureObserved: true }) ClearFlight();
        }
        SectorHistory.Clear();
        if (_coordinator?.Duty is { } duty)
            foreach (var sector in duty.Sectors) SectorHistory.Add($"{sector.Number:00} · {sector.Flight.Callsign} · {sector.Flight.Origin} → {sector.Flight.Destination} · {sector.Airline} r{sector.ProfileRevision} · {sector.Dispatcher.Persona.Name} · {(sector.HandlingCompleted ? "Handling completed" : sector.Archived ? "Archived incomplete" : "Handling active")}");
        foreach (string name in new[] { nameof(HasRecovery), nameof(RecoveryTitle), nameof(RecoveryDetail), nameof(RecoveryButton), nameof(HasGsxShortcut), nameof(ConnectionLabel), nameof(HasOperation), nameof(HandlingActive), nameof(HasWorkspace), nameof(CanChat), nameof(ChatStatus), nameof(NeedsSetup), nameof(ShowWelcome), nameof(WelcomeMessage), nameof(LoadLabel), nameof(DutyLabel), nameof(ArrivalLabel), nameof(NextFlightLabel), nameof(FlightLabel), nameof(ProfileLabel), nameof(Compatibility), nameof(ServiceDetails), nameof(RealismDetails) }) Raise(name);
        foreach (var command in new[] { StartCommand, ReconcileCommand, GateCommand, SendCommand, EndCommand, ClearChatCommand, PrepareNextCommand, EndDutyCommand, CompleteSetupCommand, RecoveryCommand }) command.Refresh();
        QuickReplyCommand.Refresh();
        if (_automatic && AutoConnect && !_connected && (_worker is null || _worker.IsCompleted) && _clock.UtcNow - _lastConnectionAttempt > TimeSpan.FromSeconds(10) && ConnectCommand.CanExecute(null)) ConnectCommand.Execute(null);
        if (_prepareWhenReady && !HasOperation && _flight is not null && AutomationDisabled && IntegrationReviewed && FlightConfirmed &&
            _gateway?.Connection == ConnectionState.Connected && _simulator is { Source: EvidenceSource.SimConnect, Connection: ConnectionState.Connected } simulator &&
            DateTimeOffset.UtcNow - simulator.ObservedAt < TimeSpan.FromSeconds(5) && StartCommand.CanExecute(null))
        {
            _prepareWhenReady = false;
            StartCommand.Execute(null);
        }
    }
    private AsyncRelayCommand Command(Func<Task> execute, Func<bool>? canExecute = null) => new(async () => { await execute(); _commandFailed = false; Refresh(); },
        exception => { _commandFailed = true; Status = exception.Message; Refresh(); }, canExecute);
    private LiveDispatchCoordinator Required() => _coordinator ?? throw new InvalidOperationException("Connect to the simulator first.");
    private SimulatorSnapshot Simulator() => _simulator ?? throw new InvalidOperationException("Waiting for live simulator telemetry.");
    public async Task StopAsync()
    {
        if (_stop is not null) await _stop.CancelAsync().ConfigureAwait(false);
        if (_worker is not null) await _worker.ConfigureAwait(false);
        if (_gateway is not null) await _gateway.DisposeAsync().ConfigureAwait(false);
        _stop?.Dispose(); _stop = null; _worker = null; _connected = false; _prepareWhenReady = false;
    }
    public void Dispose() { _refreshTimer?.Stop(); StopAsync().GetAwaiter().GetResult(); }
}
