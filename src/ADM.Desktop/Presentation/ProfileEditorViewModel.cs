using System.Collections.Immutable;
using System.Collections.ObjectModel;
using ADM.Application;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Desktop.Presentation;

public sealed record ProfileScope(string Key, string Label) { public override string ToString() => Label; }
public enum OverrideMode { Inherit, Automatic, Ask, Disabled }
public sealed record OperationOption(FlightOperationType Value, string Label)
{
    public override string ToString() => Label;
}
public sealed record AirlineOption(string Icao, string Name)
{
    public string Label => $"{Icao} · {Name}";
    public override string ToString() => Label;
}
public sealed record PlanPreviewRow(string Service, string Policy, string Source);
public sealed class PolicyEditorRow(ServiceKind kind, ServicePolicy airline, OverrideMode family,
    OverrideMode type, Action changed, int phase = 3, string scope = "airline") : ObservableObject
{
    private ServicePolicy _airline = airline;
    private OverrideMode _family = family;
    private OverrideMode _type = type;
    private int _phase = phase;
    public ServiceKind Kind { get; } = kind;
    public string Name => Kind.ToString();
    public IReadOnlyList<OverrideMode> Modes { get; } = scope == "airline" ? [OverrideMode.Automatic, OverrideMode.Ask, OverrideMode.Disabled] : Enum.GetValues<OverrideMode>();
    public OverrideMode Mode
    {
        get => scope switch { "type" => Type, "family" => Family, _ => Enum.Parse<OverrideMode>(Airline.ToString()) };
        set { if (scope == "type") Type = value; else if (scope == "family") Family = value; else Airline = Enum.Parse<ServicePolicy>(value.ToString()); Raise(); }
    }
    public ServicePolicy Airline { get => _airline; set { if (Set(ref _airline, value)) changed(); } }
    public OverrideMode Family { get => _family; set { if (Set(ref _family, value)) changed(); } }
    public OverrideMode Type { get => _type; set { if (Set(ref _type, value)) changed(); } }
    public int Phase { get => _phase; set { if (Set(ref _phase, value)) changed(); } }
    public bool CanChangePhase => Kind != ServiceKind.Boarding;
}

/// <summary>Validated immutable profile candidate. Saving is owned by the application; active plans never change.</summary>
public sealed class ProfileEditorViewModel : ObservableObject
{
    private readonly IServicePlanResolver _resolver;
    private readonly Dictionary<(string Airline, FlightOperationType Operation, ServiceKind Service, string Type, string Family), (ServicePolicy Airline, OverrideMode Family, OverrideMode Type)> _edits = [];
    private readonly Dictionary<(string Airline, FlightOperationType Operation, string Scope), ImmutableDictionary<ServiceKind, int>> _phaseDrafts = [];
    private AirlineOption _airline;
    private OperationOption _operation;
    private string _aircraftType = "A20N";
    private string _aircraftFamily = "A320_FAMILY";
    private ImmutableDictionary<string, AirlineProfile> _savedProfiles = ImmutableDictionary<string, AirlineProfile>.Empty;
    private readonly Dictionary<string, AirlineProfile> _importedProfiles = [];
    private readonly Dictionary<string, (string Probability, string Maximum)> _realismDrafts = [];
    private AirlineProfile? SelectedProfile => _importedProfiles.GetValueOrDefault(SelectedAirline.Icao) ?? _savedProfiles.GetValueOrDefault(SelectedAirline.Icao);
    public string DelayProbabilityText
    {
        get => _realismDrafts.TryGetValue(SelectedAirline.Icao, out var value) ? value.Probability : SelectedProfile?.Realism?.ProbabilityPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        set { _realismDrafts[SelectedAirline.Icao] = (value, MaximumDelayText); Rebuild(); Raise(); }
    }
    public string MaximumDelayText
    {
        get => _realismDrafts.TryGetValue(SelectedAirline.Icao, out var value) ? value.Maximum : SelectedProfile?.Realism?.MaximumDelayMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        set { _realismDrafts[SelectedAirline.Icao] = (DelayProbabilityText, value); Rebuild(); Raise(); }
    }
    public void StageImportedProfile(AirlineProfile profile)
    {
        ProfileJsonCodec.Validate(profile);
        var choice = EnsureAirline(profile.Icao, profile.DisplayName);
        _importedProfiles[profile.Icao] = profile with { Revision = _savedProfiles.GetValueOrDefault(profile.Icao)?.Revision ?? 1 };
        _edits.Clear(); _phaseDrafts.Clear(); _realismDrafts.Remove(profile.Icao); _airline = choice; Raise(nameof(SelectedAirline)); LoadRows();
        SequenceStatus = "Imported profile staged for review. Save profile to apply it to future operations."; Raise(nameof(SequenceStatus));
    }
    public AirlineProfile? EditedProfile { get; private set; }
    public string AircraftType { get => _aircraftType; set { if (_aircraftType != value.Trim().ToUpperInvariant()) { KeepDraft(); _aircraftType = value.Trim().ToUpperInvariant(); Raise(); LoadRows(); } } }
    public string AircraftFamily { get => _aircraftFamily; set { if (_aircraftFamily != value.Trim().ToUpperInvariant()) { KeepDraft(); _aircraftFamily = value.Trim().ToUpperInvariant(); Raise(); LoadRows(); } } }
    public void LoadProfiles(ImmutableDictionary<string, AirlineProfile> profiles) { _savedProfiles = profiles; _importedProfiles.Clear(); _realismDrafts.Clear(); _edits.Clear(); _phaseDrafts.Clear(); foreach (var profile in profiles.Values) EnsureAirline(profile.Icao, profile.DisplayName); LoadRows(); }
    public void SelectFlightProfile(string icao, string aircraftType, FlightOperationType operation)
    {
        SelectedAirline = EnsureAirline(icao, _savedProfiles.GetValueOrDefault(icao)?.DisplayName ?? icao);
        if (aircraftType.Length > 0) AircraftType = aircraftType;
        SelectedOperation = Operations.Single(option => option.Value == operation);
    }
    public ProfileEditorViewModel(IServicePlanResolver resolver, ImmutableDictionary<string, ImmutableDictionary<ServiceKind, int>>? savedSequences = null)
    {
        _resolver = resolver;
        SavedSequences = savedSequences ?? ImmutableDictionary<string, ImmutableDictionary<ServiceKind, int>>.Empty;
        SaveSequenceCommand = new(_ => SaveSequence(), _ => SequenceError.Length == 0);
        CreateProfileCommand = new(_ => CreateProfile(), _ => CanCreateProfile);
        _airline = Airlines[1];
        _operation = Operations[1];
        LoadRows();
    }
    public ObservableCollection<AirlineOption> Airlines { get; } = [new("GENERIC", "Generic preferences"), new("BAW", "British Airways"), new("RYR", "Ryanair"), new("UAL", "United Airlines")];
    public IReadOnlyList<OperationOption> Operations { get; } = [new(FlightOperationType.FirstDeparture, "First departure"), new(FlightOperationType.Turnaround, "Turnaround")];
    public IReadOnlyList<ServicePolicy> Policies { get; } = Enum.GetValues<ServicePolicy>();
    public IReadOnlyList<int> Phases { get; } = Enumerable.Range(1, 7).ToArray();
    public ImmutableDictionary<string, ImmutableDictionary<ServiceKind, int>> SavedSequences { get; private set; }
    public RelayCommand SaveSequenceCommand { get; }
    public string SequenceError { get; private set; } = "";
    public string SequenceStatus { get; private set; } = "Equal phase numbers group services. Overlap requires validated aircraft support.";
    public ImmutableDictionary<ServiceKind, int> SavedPhases(string airline, FlightOperationType operation) =>
        SavedSequences.GetValueOrDefault($"{airline}/{operation}") ?? ServiceSequence.DefaultPhases;
    public IReadOnlyList<OverrideMode> OverrideModes { get; } = Enum.GetValues<OverrideMode>();
    public ObservableCollection<PolicyEditorRow> Rows { get; } = [];
    public ObservableCollection<PlanPreviewRow> Preview { get; } = [];
    public AppliedServicePlan PreviewPlan { get; private set; } = null!;
    public AirlineOption SelectedAirline { get => _airline; set { if (_airline != value) { KeepDraft(); _airline = value; Raise(); LoadRows(); } } }
    public OperationOption SelectedOperation { get => _operation; set { if (_operation != value) { KeepDraft(); _operation = value; Raise(); LoadRows(); } } }
    public string PreviewTitle => $"{SelectedAirline.Icao} / {AircraftType} / {SelectedOperation.Label}";
    public IReadOnlyList<ProfileScope> Scopes { get; } = [new("airline", "All aircraft in this airline"), new("type", "One aircraft type")];
    private ProfileScope _scope = new("airline", "All aircraft in this airline");
    public ProfileScope SelectedScope { get => _scope; set { if (_scope != value) { KeepDraft(); _scope = value; Raise(); Raise(nameof(IsAircraftScope)); Raise(nameof(IsFamilyScope)); LoadRows(); } } }
    public bool IsAircraftScope => SelectedScope.Key != "airline";
    public bool IsFamilyScope => SelectedScope.Key == "family";
    private string ScopeKey => SelectedScope.Key == "airline" ? "airline" : SelectedScope.Key + ":" + (IsFamilyScope ? AircraftFamily : AircraftType);
    public IReadOnlyList<string> AircraftChoices => new[] { AircraftType }.Concat(SelectedProfile?.TypeOverrides.Keys ?? []).Where(key => key.Length > 0).Distinct().Order().ToArray();
    private string _newCode = "", _newName = "", _creationStatus = "";
    public string NewAirlineCode { get => _newCode; set { Set(ref _newCode, value.Trim().ToUpperInvariant()); Raise(nameof(CanCreateProfile)); CreateProfileCommand.Refresh(); } }
    public string NewAirlineName { get => _newName; set { Set(ref _newName, value.Trim()); Raise(nameof(CanCreateProfile)); CreateProfileCommand.Refresh(); } }
    public bool CanCreateProfile => NewAirlineCode.Length == 3 && NewAirlineCode.All(character => character is >= 'A' and <= 'Z') && NewAirlineName.Length is > 0 and <= 100 && Airlines.All(airline => airline.Icao != NewAirlineCode);
    public string CreationStatus { get => _creationStatus; private set => Set(ref _creationStatus, value); }
    public bool CopyCurrentProfile { get; set; }
    public RelayCommand CreateProfileCommand { get; }
    private AirlineOption EnsureAirline(string code, string name)
    {
        var existing = Airlines.SingleOrDefault(airline => airline.Icao == code);
        if (existing is not null) return existing;
        var choice = new AirlineOption(code, name); Airlines.Add(choice); return choice;
    }
    private void KeepDraft() { if (EditedProfile is { } profile && SequenceError.Length == 0) _importedProfiles[profile.Icao] = profile; }
    private void CreateProfile()
    {
        if (!CanCreateProfile) return;
        KeepDraft();
        var baseline = CopyCurrentProfile ? EditedProfile! : _savedProfiles.GetValueOrDefault("GENERIC") ?? SampleSessionFactory.GenericProfile();
        var profile = baseline with { Icao = NewAirlineCode, DisplayName = NewAirlineName, Revision = 1 };
        StageImportedProfile(profile);
        CreationStatus = $"{profile.DisplayName} created as a draft. Choose its services, then Save profile.";
        NewAirlineCode = ""; NewAirlineName = "";
    }
    private void LoadRows()
    {
        Rows.Clear();
        foreach (ServiceKind kind in ServiceSequence.DefaultOrder.Where(kind => kind != ServiceKind.Deboarding))
        {
            var defaults = SelectedOperation.Value == FlightOperationType.FinalArrival
                ? (ServicePolicy.Disabled, OverrideMode.Inherit, OverrideMode.Inherit)
                : (kind == ServiceKind.Cleaning ? ServicePolicy.Disabled : ServicePolicy.Automatic,
                    OverrideMode.Inherit, OverrideMode.Inherit);
            var saved = SelectedProfile;
            var genericRule = _savedProfiles.GetValueOrDefault("GENERIC")?.Operations.GetValueOrDefault(SelectedOperation.Value)?.Services.GetValueOrDefault(kind);
            if (genericRule is not null) defaults.Item1 = genericRule.Policy;
            if (saved is not null)
            {
                var policy = saved.Operations.GetValueOrDefault(SelectedOperation.Value);
                defaults.Item1 = policy?.Services.GetValueOrDefault(kind)?.Policy ?? defaults.Item1;
                var family = saved.FamilyOverrides.GetValueOrDefault(AircraftFamily)?.Operations.GetValueOrDefault(SelectedOperation.Value)?.Services.GetValueOrDefault(kind);
                var type = saved.TypeOverrides.GetValueOrDefault(AircraftType)?.Operations.GetValueOrDefault(SelectedOperation.Value)?.Services.GetValueOrDefault(kind);
                defaults.Item2 = family is null ? OverrideMode.Inherit : Enum.Parse<OverrideMode>(family.Policy.ToString());
                defaults.Item3 = type is null ? OverrideMode.Inherit : Enum.Parse<OverrideMode>(type.Policy.ToString());
            }
            var values = _edits.GetValueOrDefault((SelectedAirline.Icao, SelectedOperation.Value, kind, AircraftType, AircraftFamily), defaults);
            var phases = _phaseDrafts.GetValueOrDefault((SelectedAirline.Icao, SelectedOperation.Value, ScopeKey)) ??
                (SelectedScope.Key == "type" ? saved?.TypeOverrides.GetValueOrDefault(AircraftType)?.Operations.GetValueOrDefault(SelectedOperation.Value)?.ServicePhases : SelectedScope.Key == "family" ? saved?.FamilyOverrides.GetValueOrDefault(AircraftFamily)?.Operations.GetValueOrDefault(SelectedOperation.Value)?.ServicePhases : null) ??
                saved?.Operations.GetValueOrDefault(SelectedOperation.Value)?.ServicePhases ?? SavedPhases(SelectedAirline.Icao, SelectedOperation.Value);
            Rows.Add(new(kind, values.Item1, values.Item2, values.Item3, Rebuild, phases[kind], SelectedScope.Key));
        }
        Rebuild(); Raise(nameof(AircraftChoices)); Raise(nameof(DelayProbabilityText)); Raise(nameof(MaximumDelayText));
    }
    private void Rebuild()
    {
        _phaseDrafts[(SelectedAirline.Icao, SelectedOperation.Value, ScopeKey)] = Rows.ToImmutableDictionary(row => row.Kind, row => row.Phase);
        var generic = _importedProfiles.GetValueOrDefault("GENERIC") ?? _savedProfiles.GetValueOrDefault("GENERIC") ?? SampleSessionFactory.GenericProfile();
        var savedProfile = SelectedProfile;
        var savedOperation = savedProfile?.Operations.GetValueOrDefault(SelectedOperation.Value);
        var savedFamily = savedProfile?.FamilyOverrides.GetValueOrDefault(AircraftFamily)?.Operations.GetValueOrDefault(SelectedOperation.Value);
        var savedType = savedProfile?.TypeOverrides.GetValueOrDefault(AircraftType)?.Operations.GetValueOrDefault(SelectedOperation.Value);
        var airlineRules = ImmutableDictionary.CreateBuilder<ServiceKind, ServiceRule>();
        var familyRules = ImmutableDictionary.CreateBuilder<ServiceKind, ServiceRule>();
        var typeRules = ImmutableDictionary.CreateBuilder<ServiceKind, ServiceRule>();
        foreach (var row in Rows)
        {
            _edits[(SelectedAirline.Icao, SelectedOperation.Value, row.Kind, AircraftType, AircraftFamily)] = (row.Airline, row.Family, row.Type);
            airlineRules[row.Kind] = Rule(row.Kind, row.Airline, savedOperation?.Services.GetValueOrDefault(row.Kind) ?? generic.Operations[SelectedOperation.Value].Services.GetValueOrDefault(row.Kind));
            if (row.Family != OverrideMode.Inherit) familyRules[row.Kind] = Rule(row.Kind, ToPolicy(row.Family), savedFamily?.Services.GetValueOrDefault(row.Kind));
            if (row.Type != OverrideMode.Inherit) typeRules[row.Kind] = Rule(row.Kind, ToPolicy(row.Type), savedType?.Services.GetValueOrDefault(row.Kind));
        }
        var profile = generic with
        {
            Icao = SelectedAirline.Icao,
            DisplayName = SelectedAirline.Name,
            Operations = ForOperation(airlineRules.ToImmutable()),
            FamilyOverrides = ImmutableDictionary<string, AircraftProfileOverride>.Empty.Add(AircraftFamily, new(AircraftFamily, ForOperation(familyRules.ToImmutable(), SelectedScope.Key == "family" ? Rows.ToImmutableDictionary(row => row.Kind, row => row.Phase).Add(ServiceKind.Deboarding, 1) : savedFamily?.ServicePhases))),
            TypeOverrides = ImmutableDictionary<string, AircraftProfileOverride>.Empty.Add(AircraftType, new(AircraftType, ForOperation(typeRules.ToImmutable(), SelectedScope.Key == "type" ? Rows.ToImmutableDictionary(row => row.Kind, row => row.Phase).Add(ServiceKind.Deboarding, 1) : savedType?.ServicePhases)))
        };
        try
        {
            var phases = Rows.ToImmutableDictionary(row => row.Kind, row => row.Phase).Add(ServiceKind.Deboarding, 1);
            profile = profile with
            {
                Operations = profile.Operations.SetItem(SelectedOperation.Value,
                profile.Operations[SelectedOperation.Value] with { ServicePhases = SelectedScope.Key == "airline" ? phases : savedOperation?.ServicePhases ?? SavedPhases(SelectedAirline.Icao, SelectedOperation.Value) })
            };
            PreviewPlan = _resolver.Resolve(generic, profile, new(AircraftType, AircraftFamily), SelectedOperation.Value,
                new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty));
            var previous = _savedProfiles.GetValueOrDefault(SelectedAirline.Icao);
            var baseline = _importedProfiles.GetValueOrDefault(SelectedAirline.Icao) ?? previous ?? generic with { Icao = SelectedAirline.Icao, DisplayName = SelectedAirline.Name };
            var families = baseline.FamilyOverrides;
            var familyOperations = families.GetValueOrDefault(AircraftFamily)?.Operations ?? ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty;
            families = families.SetItem(AircraftFamily, new(AircraftFamily, familyOperations.SetItem(SelectedOperation.Value, profile.FamilyOverrides[AircraftFamily].Operations[SelectedOperation.Value])));
            var types = baseline.TypeOverrides;
            var typeOperations = types.GetValueOrDefault(AircraftType)?.Operations ?? ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty;
            types = types.SetItem(AircraftType, new(AircraftType, typeOperations.SetItem(SelectedOperation.Value, profile.TypeOverrides[AircraftType].Operations[SelectedOperation.Value])));
            EditedProfile = baseline with
            {
                Revision = previous is null && SelectedAirline.Icao != "GENERIC" ? 1 : (previous?.Revision ?? baseline.Revision) + 1,
                Realism = new(ParseOptional(DelayProbabilityText), ParseOptional(MaximumDelayText)),
                Operations = baseline.Operations.SetItem(SelectedOperation.Value, profile.Operations[SelectedOperation.Value]),
                FamilyOverrides = families,
                TypeOverrides = types
            };
            ProfileJsonCodec.Validate(EditedProfile);
            SequenceError = "";
        }
        catch (ArgumentException exception)
        {
            SequenceError = exception.Message;
            Raise(nameof(SequenceError)); SaveSequenceCommand.Refresh(); return;
        }
        Preview.Clear();
        foreach (var kind in ServiceSequence.Ordered(PreviewPlan).Where(kind => kind != ServiceKind.Deboarding))
        {
            var resolved = PreviewPlan.Services[kind];
            Preview.Add(new($"{PreviewPlan.ServicePhases[kind]} · {resolved.Kind}", resolved.Rule.Policy.ToString(), resolved.Source switch
            { PolicyLayer.AircraftType => $"{AircraftType} override", PolicyLayer.AircraftFamily => $"{AircraftFamily} family", _ => $"{SelectedAirline.Icao} profile" }));
        }
        Raise(nameof(SequenceError)); SaveSequenceCommand.Refresh();
        Raise(nameof(PreviewPlan));
        Raise(nameof(PreviewTitle));
    }
    private void SaveSequence()
    {
        SavedSequences = SavedSequences.SetItem($"{SelectedAirline.Icao}/{SelectedOperation.Value}", PreviewPlan.ServicePhases);
        SequenceStatus = "Service order saved for the next operation. Active handling keeps its current plan.";
        Raise(nameof(SequenceStatus)); Raise(nameof(SavedSequences));
    }
    private static int? ParseOptional(string value) => string.IsNullOrWhiteSpace(value) ? null : int.TryParse(value, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? parsed : throw new ArgumentException("Enter a whole number for realism tuning, or leave it blank to inherit.");
    private ImmutableDictionary<FlightOperationType, OperationPolicy> ForOperation(ImmutableDictionary<ServiceKind, ServiceRule> rules,
        ImmutableDictionary<ServiceKind, int>? phases = null) =>
        ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty.Add(SelectedOperation.Value, new(rules) { ServicePhases = phases });
    private static ServiceRule Rule(ServiceKind kind, ServicePolicy policy, ServiceRule? previous = null) => new(policy,
        policy != ServicePolicy.Disabled && (kind is ServiceKind.Refuelling or ServiceKind.Boarding || previous?.Required == true),
        previous?.Dependencies ?? [], previous?.ApplicabilityRule ?? "Pilot service preference; operational guards remain mandatory", previous?.Applicability ?? ServiceApplicability.AllOperations);
    private static ServicePolicy ToPolicy(OverrideMode mode) => mode switch
    { OverrideMode.Automatic => ServicePolicy.Automatic, OverrideMode.Ask => ServicePolicy.Ask, _ => ServicePolicy.Disabled };
}
