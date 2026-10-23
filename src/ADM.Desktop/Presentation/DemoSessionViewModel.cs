using System.Collections.Immutable;
using System.Collections.ObjectModel;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;

namespace ADM.Desktop.Presentation;

/// <summary>Presentation adapter over M2's local use case. Observations are explicitly simulated, never real GSX calls.</summary>
public sealed class DemoSessionViewModel : ObservableObject, IDisposable
{
    private readonly ChatDeliveryQueue _delivery;
    private readonly System.Windows.Threading.DispatcherTimer? _deliveryTimer;
    private int _queuedMessages;
    private bool _chatInitialized;
    public event Action? MessageSent;
    private readonly IOperationalClock _clock;
    private readonly Action _notify;
    private readonly IConversationEngine _conversation;
    private readonly PersonaCatalogue _personas;
    private LocalDispatchSession _runtime = null!;
    private string _draft = "";
    private PendingServiceAction? _pendingAction;

    public DemoSessionViewModel(IOperationalClock clock, Action notify, IConversationEngine conversation, PersonaCatalogue personas,
        Func<ImmutableDictionary<ServiceKind, int>>? nextSequence = null, ChatTimingOptions? chatTiming = null)
    {
        var timing = chatTiming ?? new(false); _delivery = new(timing.NextDelay);
        if (timing.Enabled && System.Windows.Application.Current is { } application)
        {
            _deliveryTimer = new(TimeSpan.FromMilliseconds(100), System.Windows.Threading.DispatcherPriority.Background, (_, _) => FlushChat(), application.Dispatcher);
            _deliveryTimer.Stop();
        }
        _clock = clock; _notify = notify; _conversation = conversation; _personas = personas;
        NextSequence = nextSequence;
        SendCommand = new(_ => Send(Draft), _ => !string.IsNullOrWhiteSpace(Draft));
        QuickReplyCommand = new(value => Send(QuickChat.Message(value as string ?? "", _runtime.Context, _runtime.Checkpoint.Messages.Count(message => message.Sender == "You"))));
        AdvanceCommand = new(_ => Advance(), _ => CanAdvance);
        ResetCommand = new(_ => Reset());
        SkipCommand = new(value => Propose(value, "Skip"), value => value is ServiceRow row && Services.Contains(row) && row.CanSkip && CanEditServices);
        CancelCommand = new(value => Propose(value, "Cancel"), value => value is ServiceRow row && Services.Contains(row) && row.CanCancel && CanEditServices);
        ResumeCommand = new(value => Propose(value, "Resume"), value => value is ServiceRow row && Services.Contains(row) && row.CanResume && CanEditServices);
        ObserveCancellationCommand = new(value =>
        {
            if (value is ServiceRow row) Accept(new ObserveService(Meta("Sample cancellation observation"), row.Kind, ServiceState.Cancelled));
        }, value => value is ServiceRow row && Services.Contains(row) && row.CanObserveCancellation && !IsInterrupted);
        ConfirmActionCommand = new(_ => ConfirmAction(), _ => PendingAction is not null && !IsInterrupted);
        DismissActionCommand = new(_ => { _pendingAction = null; Update(); });
        Reset();
    }
    public ObservableCollection<DemoMessage> Messages { get; } = [];
    public ObservableCollection<ServiceRow> Services { get; } = [];
    public ObservableCollection<string> QuickReplies { get; } = [];
    public ObservableCollection<DemoApproval> Approvals { get; } = [];
    public IReadOnlyList<DemoSector> History { get; } = [new("01", "Edinburgh → London", "Completed · sample history", false), new("02", "London → Amsterdam", "Current ground operation · demo", true)];
    public DispatchCheckpoint Checkpoint => _runtime.Checkpoint;
    public Func<ImmutableDictionary<ServiceKind, int>>? NextSequence { get; init; }
    private OperationalState State => Checkpoint.State;
    public Guid DemoId => State.Session.Id;
    public string Dispatcher => State.Session.Dispatcher.Persona.Name;
    public string Initials => Dispatcher[..1];
    public string PersonaDescription => (State.Session.Dispatcher.Persona.Tone switch
    { "WarmProfessional" => "Warm professional", "FormalConcise" => "Formal", _ => "Friendly" }) +
        (State.Session.Dispatcher.Persona.Language == "en-US" ? " · English (US)" : " · English (UK)");
    public RelayCommand SendCommand { get; }
    public RelayCommand QuickReplyCommand { get; }
    public RelayCommand AdvanceCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand SkipCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand ObserveCancellationCommand { get; }
    public RelayCommand ConfirmActionCommand { get; }
    public RelayCommand DismissActionCommand { get; }
    public DemoStage Stage => State.Session.Stage switch
    {
        WorkflowStage.AwaitingBoardingApproval => DemoStage.BoardingApproval,
        WorkflowStage.Boarding => DemoStage.Boarding,
        WorkflowStage.AwaitingClosureApproval => DemoStage.ClosureApproval,
        WorkflowStage.ClosingAndReleasing => DemoStage.Releasing,
        WorkflowStage.Completed => DemoStage.Complete,
        WorkflowStage.NeedsAttention or WorkflowStage.Suspended => DemoStage.NeedsReview,
        _ => DemoStage.Preparation
    };
    public string Draft { get => _draft; set { if (Set(ref _draft, value)) SendCommand.Refresh(); } }
    public bool IsInterrupted => State.Connection != ConnectionState.Connected;
    public bool ShowRecovery => IsInterrupted || Stage == DemoStage.NeedsReview;
    public string RecoveryText => IsInterrupted ? "Connection preview paused. Observations need reconciliation; no commands will be replayed." : "Review the current service states before continuing.";
    public string StageLabel => Stage switch
    {
        DemoStage.Preparation => "Ground services",
        DemoStage.BoardingApproval => "Boarding approval",
        DemoStage.Boarding => "Boarding",
        DemoStage.ClosureApproval => "Closure approval",
        DemoStage.Releasing => "Closing & disconnecting",
        DemoStage.Complete => "Ground handling complete",
        _ => "Needs review"
    };
    public string GateLabel => State.Session.Gate.State switch
    {
        GateReadinessState.Ready => State.Session.Gate.Provenance == GateProvenance.Simulation ? "Gate ready · simulated" : "Gate ready · pilot confirmed",
        GateReadinessState.Held => "Gate hold",
        GateReadinessState.Preparing => "Gate preparing",
        _ => "Gate unknown"
    };
    public string Overview => Stage == DemoStage.Complete ? "Ground handling complete. Pushback is yours." : OperationalReducer.DelayDescription(State);
    public string AdvanceLabel => Stage switch
    {
        DemoStage.Preparation => "Finish sample services",
        DemoStage.Boarding => "Finish sample boarding",
        DemoStage.Releasing => "Observe sample release",
        DemoStage.Complete => "Demo complete",
        _ => "Waiting for your reply"
    };
    public bool CanAdvance => !IsInterrupted && PendingAction is null &&
        (Stage == DemoStage.Preparation && State.Session.Services.Values.Any(service => service.Kind != ServiceKind.Boarding && service.State is ServiceState.Active or ServiceState.RequestPending or ServiceState.Requested) ||
         Stage == DemoStage.Boarding && State.Session.Services[ServiceKind.Boarding].State == ServiceState.Active ||
         Stage == DemoStage.Releasing && State.Question?.Kind != QuestionKind.ManualRelease);
    public bool CanEditServices => !IsInterrupted && Stage is DemoStage.Preparation or DemoStage.Boarding or DemoStage.NeedsReview;
    public bool HasQuickReplies => QuickReplies.Count > 0;
    public PendingServiceAction? PendingAction => _pendingAction;
    public bool HasPendingAction => PendingAction is not null;
    public string CompletedSummary => $"{Services.Count(row => row.State is ServiceState.Completed or ServiceState.NotRequired or ServiceState.Skipped)} of 6 services resolved";

    public void Reset()
    {
        Guid sessionId = Guid.NewGuid();
        var baseline = SampleSessionFactory.GenericProfile();
        var rules = baseline.Operations[FlightOperationType.Turnaround].Services.ToImmutableDictionary(pair => pair.Key, pair =>
            pair.Value with
            {
                Policy = pair.Key == ServiceKind.Deboarding ? ServicePolicy.Disabled : ServicePolicy.Automatic,
                Required = pair.Key is ServiceKind.Refuelling or ServiceKind.Boarding,
                Dependencies = ImmutableHashSet<ServiceKind>.Empty
            });
        var plan = new AppliedServicePlan(Guid.NewGuid(), "BAW", 1, new("A20N", "A320_FAMILY"), FlightOperationType.Turnaround,
            rules.ToImmutableDictionary(pair => pair.Key, pair => new ResolvedServiceRule(pair.Key, pair.Value, PolicyLayer.Airline, "BAW", 1)), _clock.UtcNow)
        { ServicePhases = NextSequence?.Invoke() ?? ServiceSequence.DefaultPhases };
        var state = OperationalFactory.Create(sessionId, Guid.NewGuid(), plan, new(sessionId, _personas.Select(sessionId), _clock.UtcNow),
            Enum.GetValues<ServiceKind>().ToImmutableHashSet(), Enum.GetValues<ServiceKind>().ToImmutableHashSet(), Enum.GetValues<ReleaseAction>().ToImmutableHashSet()) with
        { ValidatedConcurrentGroups = [ImmutableHashSet.Create(ServiceKind.Water, ServiceKind.Lavatory, ServiceKind.Refuelling, ServiceKind.Catering)] };
        _runtime = new(state, _conversation, _clock);
        var gateEvidence = new Evidence(EvidenceSource.Simulation, "Sample gate check", _clock.UtcNow);
        _runtime.Accept(new ObserveGate(Meta("Sample gate check"), new(GateReadinessState.Ready, GateProvenance.Simulation, gateEvidence)));
        for (int step = 0; step < 20; step++)
        {
            StartPending();
            var active = State.Session.Services.Values.FirstOrDefault(service => service.State == ServiceState.Active);
            if (active is null || State.Session.Services[ServiceKind.Refuelling].State == ServiceState.Active) break;
            _runtime.Accept(new ObserveService(Meta("Sample service completion"), active.Kind, ServiceState.Completed));
        }
        _pendingAction = null;
        Services.Clear();
        foreach (var kind in ServiceSequence.Ordered(plan).Where(kind => kind != ServiceKind.Deboarding)) Services.Add(new(kind, State.Session.Services[kind].State, "", plan.ServicePhases[kind]));
        Messages.Clear();
        _delivery.Clear(); _queuedMessages = 0; _chatInitialized = false;
        Draft = "";
        Update();
    }
    private EventMetadata Meta(string evidence) => _runtime.Metadata(EvidenceSource.Simulation, evidence);
    private void StartPending()
    {
        foreach (var command in State.Commands.Where(command => !State.AcknowledgedCommands.Contains(command.Id)).ToArray())
        {
            if (!OperationalCommandPolicy.CanDispatch(State, command)) continue;
            _runtime.Accept(new AcknowledgeCommand(Meta("Sample receipt; not completion"), command.Id, CommandOutcome.Accepted));
            if (command.Kind == OperationalCommandKind.RequestService && command.Service is { } kind && State.Session.Services[kind].State == ServiceState.Requested)
                _runtime.Accept(new ObserveService(Meta("Sample service active"), kind, ServiceState.Active));
        }
    }
    private void Advance()
    {
        if (Stage == DemoStage.Preparation)
        {
            for (int step = 0; step < 20; step++)
            {
                StartPending();
                var active = State.Session.Services.Values.FirstOrDefault(service => service.Kind != ServiceKind.Boarding && service.State == ServiceState.Active);
                if (active is null) break;
                Accept(new ObserveService(Meta("Explicit sample completion"), active.Kind, ServiceState.Completed));
            }
        }
        else if (Stage == DemoStage.Boarding) Accept(new ObserveService(Meta("All sample passengers boarded"), ServiceKind.Boarding, ServiceState.Completed));
        else if (Stage == DemoStage.Releasing)
        {
            foreach (var action in Enum.GetValues<ReleaseAction>())
                if (!State.Releases[action].IsSatisfied && State.SupportedReleaseActions.Contains(action))
                    Accept(new ObserveRelease(Meta("Explicit sample equipment release"), action));
        }
        Update();
    }
    public void Send(string message)
    {
        _runtime.Send(message);
        Draft = "";
        StartPending();
        Update();
    }
    private void Accept(OperationEvent input)
    {
        _runtime.Accept(input);
        Update();
    }
    public void PreviewConnectionLoss(bool disconnected)
    {
        Accept(new ChangeConnection(_runtime.Metadata(EvidenceSource.System), disconnected ? ConnectionState.Offline : ConnectionState.Connected));
        _pendingAction = null;
        Update();
    }
    private void Propose(object? value, string action)
    {
        if (value is ServiceRow row)
        {
            _pendingAction = new(row, action, action == "Cancel" ? $"Request cancellation of {row.Name.ToLowerInvariant()}? Confirmation remains pending until a separate observation." :
                $"{action} {row.Name.ToLowerInvariant()} for this flight?");
            Update();
        }
    }
    private void ConfirmAction()
    {
        if (PendingAction is not { } action) return;
        _pendingAction = null;
        Send($"{action.Action} {action.Service.Kind}");
    }
    private void Update()
    {
        while (_queuedMessages < Checkpoint.Messages.Length)
        {
            _delivery.Enqueue(Checkpoint.Messages[_queuedMessages++], DateTimeOffset.UtcNow, _chatInitialized);
        }
        FlushChat(); _chatInitialized = true;
        foreach (var row in Services)
        {
            var service = State.Session.Services[row.Kind];
            row.State = service.State;
            row.Detail = service.State switch
            {
                ServiceState.Completed => "Sample observation recorded",
                ServiceState.Active => "Sample service in progress",
                ServiceState.CancelPending => "Awaiting cancellation observation",
                ServiceState.NotRequired => "Not required by the active profile",
                ServiceState.Unknown => "Observation needs reconciliation",
                ServiceState.Skipped => "Skipped by pilot",
                ServiceState.Cancelled => "Cancellation observed",
                ServiceState.Failed => "Needs review before retry",
                _ => "Waiting for earlier services"
            };
        }
        Approvals.Clear();
        foreach (var approval in State.Session.Approvals.Where(approval => !approval.IsInvalidated)) Approvals.Add(new(approval.Kind, approval.ApprovedAt, approval.SessionId));
        QuickReplies.Clear();
        if (!ShowRecovery && Stage != DemoStage.Complete && !_delivery.HasPendingDispatcher) foreach (string reply in LocalConversationEngine.QuickReplies(_runtime.Context)) QuickReplies.Add(reply);
        foreach (string property in new[] { nameof(Stage), nameof(StageLabel), nameof(GateLabel), nameof(Overview), nameof(AdvanceLabel), nameof(CanAdvance), nameof(CompletedSummary), nameof(HasQuickReplies), nameof(IsInterrupted), nameof(ShowRecovery), nameof(RecoveryText), nameof(CanEditServices), nameof(PendingAction), nameof(HasPendingAction), nameof(Dispatcher), nameof(Initials), nameof(PersonaDescription) }) Raise(property);
        foreach (var command in new[] { AdvanceCommand, SkipCommand, CancelCommand, ResumeCommand, ObserveCancellationCommand, ConfirmActionCommand }) command.Refresh();
    }
    private void FlushChat()
    {
        bool wasPending = _delivery.HasPendingDispatcher;
        bool incoming = false;
        foreach (var delivered in _delivery.Drain(DateTimeOffset.UtcNow, message => ChatDeliveryQueue.IsRelevant(message, State.Question)))
        {
            var message = delivered.Message;
            Messages.Add(new(message.Sender, message.Text, message.SentAt, message.Sender == "You") { IsSystem = message.IsSystem });
            if (delivered.Notify && message.Sender == "You") MessageSent?.Invoke();
            if (delivered.Notify && ConversationPresentation.ShouldNotify(message)) incoming = true;
        }
        if (incoming) _notify();
        if (_delivery.HasPendingDispatcher) _deliveryTimer?.Start(); else _deliveryTimer?.Stop();
        if (wasPending && !_delivery.HasPendingDispatcher && _deliveryTimer is not null) Update();
    }
    public void Dispose() { _deliveryTimer?.Stop(); _delivery.Clear(); }
}
