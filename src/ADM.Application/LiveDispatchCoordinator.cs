using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application;

/// <summary>Single serialized operational owner. The durable send reservation precedes every adapter call.</summary>
public sealed class LiveDispatchCoordinator(IGsxGateway gateway, IGsxLiveState live, IDispatchStore store,
    IConversationEngine conversation, IOperationalClock clock, Guid? connectionSessionId = null, Func<SimulatorSnapshot?>? latestSimulator = null, Action<string>? diagnostic = null,
    IOperationalRealismEngine? realismEngine = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalDispatchSession? _runtime;
    private FlightPlanSnapshot? _flight;
    private string _aircraft = "";
    private long _generation;
    private bool _armed;
    private ImmutableHashSet<Guid> _attempted = [];
    private ImmutableHashSet<Guid> _rejected = [];
    private readonly Dictionary<ServiceKind, (ServiceState State, bool Waiting)> _last = [];
    private readonly HashSet<ServiceKind> _engaged = [];
    private DutyProgress? _duty;
    private DateTimeOffset _dutySavedAt;
    private readonly Dictionary<ServiceKind, DateTimeOffset> _acceptedAt = [];
    public ServiceKind? StalledService { get; private set; }
    public DutyProgress? Duty => _duty;
    public bool HasDuty => _duty is { State: not DutyState.Ended };
    public DispatchCheckpoint? Checkpoint => _runtime?.Checkpoint;
    public bool IsArmed => _armed;
    public string Status { get; private set; } = "No live operation started.";
    public FlightPlanSnapshot? Flight => _flight;
    public async Task PauseAsync(string reason, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { if (_runtime is not null) await SuspendAsync(reason, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private SavedDispatch Saved
    {
        get
        {
            bool completed = _runtime!.Checkpoint.State.Session.Stage == WorkflowStage.Completed;
            if (completed && _duty is not null) _duty = DutyTracking.Complete(_duty, _runtime.Checkpoint.State.Session.Id, true, true);
            return new(_runtime.Checkpoint, _flight!, _aircraft, completed) { Duty = _duty };
        }
    }

    public async Task ClearChatAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { if (_runtime is not null) { _runtime.ClearChat(); await store.SaveAsync(Saved, ct).ConfigureAwait(false); } }
        finally { _gate.Release(); }
    }
    public async Task EndDutyAsync(CancellationToken ct)
    {
        await EndAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { if (_duty is not null) { _duty = _duty with { State = DutyState.Ended }; if (store is IDutyStore duties) await duties.SaveDutyAsync(_duty, ct).ConfigureAwait(false); } }
        finally { _gate.Release(); }
    }

    public async Task StartAsync(FlightPlanSnapshot flight, AutomationSetup setup, AppliedServicePlan plan,
        DispatcherPersona persona, AircraftServiceCapabilities capabilities, SimulatorSnapshot simulator, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is not null || await store.LoadActiveAsync(ct).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("Restore or end the saved operation before starting another.");
            FlightPlanValidation.Validate(flight, clock.UtcNow);
            setup.Realism.Validate(); setup.GateProfile.Validate();
            if (setup.GateProfile.Icao != "GENERIC" && setup.GateProfile.Icao != flight.Origin) throw new ArgumentException("Gate profile must match the flight origin.");
            if (!setup.CompetingAutomationDisabled || !setup.AircraftIntegrationReviewed || !setup.FlightAircraftConfirmed)
                throw new InvalidOperationException("Review aircraft integration, disable competing automation and confirm the flight/aircraft before starting.");
            if (!FlightPlanValidation.Airport(setup.AirportIcao) || setup.AirportIcao != flight.Origin || setup.AircraftType != flight.Aircraft.IcaoType ||
                plan.Aircraft.IcaoType != setup.AircraftType || plan.Operation != setup.Operation || !Enum.IsDefined(setup.Operation))
                throw new ArgumentException("Flight, aircraft, airport, operation and resolved plan must match.");
            ValidateEnvironment(simulator, live.Snapshot, flight, null);
            var provider = await gateway.ProbeAsync(ct).ConfigureAwait(false);
            if (provider.IsFake || provider.VerifiedApiVersion is null) throw new InvalidOperationException("A verified real GSX connection is required.");
            _duty ??= store is IDutyStore duties ? await duties.LoadDutyAsync(ct).ConfigureAwait(false) : null;
            if (_duty is { State: not DutyState.Ended, Sectors.Length: > 0 } && !setup.SectorTransitionConfirmed)
                throw new InvalidOperationException("Confirm the arrival location and next sector before preparing handling.");
            Guid sessionId = connectionSessionId ?? Guid.NewGuid();
            if (_duty?.Sectors.Any(sector => sector.SessionId == sessionId || sector.PlanId == plan.SnapshotId) == true)
                throw new InvalidOperationException("A new sector requires a fresh session and resolved plan identity.");
            var state = OperationalFactory.Create(sessionId, Guid.NewGuid(), plan, new(sessionId, persona, clock.UtcNow),
                provider.RequestableServices.Intersect(capabilities.Services).Remove(ServiceKind.Deboarding), [], []) with
            {
                Connection = ConnectionState.ReconciliationRequired,
                ValidatedConcurrentGroups = capabilities.ConcurrentGroups,
                RequiresServiceStartApproval = true,
                GsxFlightPlanReceived = flight.Source == FlightPlanSource.Gsx,
                PlannedLoadFiguresReceived = flight.PlannedPassengers is not null && flight.PlannedZeroFuelWeightKg is not null,
                Realism = new(setup.Realism, setup.GateProfile, clock.UtcNow),
                RequestFuelAndCateringTogether = capabilities.Services.Contains(ServiceKind.Refuelling) && capabilities.Services.Contains(ServiceKind.Catering)
            };
            _duty = DutyTracking.Attach(_duty, state.Session, flight, simulator.AircraftTitle!, setup.SectorTransitionConfirmed);
            _runtime = new(state, conversation, clock); _flight = flight; _aircraft = simulator.AircraftTitle!;
            _generation = live.Snapshot.Generation; _armed = true;
            await ReconcileCoreAsync(setup.ExistingServicesBelongToFlight, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RestoreAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is not null) return true;
            _duty = store is IDutyStore duties ? await duties.LoadDutyAsync(ct).ConfigureAwait(false) : null;
            if (_duty is { State: not DutyState.Ended }) _duty = _duty with { RequiresLocationReview = true, LastObservedAt = null, AirborneSince = null, Latitude = null, Longitude = null };
            var saved = await store.LoadActiveAsync(ct).ConfigureAwait(false);
            if (saved is null) return false;
            _duty = DutyTracking.Attach(_duty, saved.Checkpoint.State.Session, saved.Flight, saved.AircraftBinding, true);
            if (saved.Checkpoint.State.Session.Stage == WorkflowStage.Completed)
            {
                _duty = DutyTracking.Complete(_duty, saved.Checkpoint.State.Session.Id, true, true);
                await store.SaveAsync(saved with { Archived = true, Duty = _duty }, ct).ConfigureAwait(false);
                Status = "Previous handling is complete and archived. Load the next flight when ready."; return false;
            }
            var savedGate = saved.Checkpoint.State.Session.Gate;
            bool ownedSimulatedGate = saved.Checkpoint.State.Realism is { } realism && savedGate.Evidence?.Source == EvidenceSource.Simulation &&
                (realism.Incident is { Status: DisruptionStatus.Active } && savedGate.State == GateReadinessState.Held ||
                    realism.Options.Enabled && realism.GateProfile.SimulatedReadiness && savedGate.State is GateReadinessState.Ready or GateReadinessState.Preparing);
            if (savedGate.Provenance == GateProvenance.Simulation && !ownedSimulatedGate || saved.Checkpoint.State.Session.Services.Values.Any(service =>
                service.CompletionProvenance == CompletionProvenance.Simulation || service.LastEvidence?.Source == EvidenceSource.Simulation))
                throw new InvalidDataException("Simulated observations cannot be restored as a live operation.");
            var restoredState = saved.Checkpoint.State;
            var legacyService = restoredState.Session.Services[ServiceKind.Deboarding];
            if (legacyService.State is ServiceState.Planned or ServiceState.Eligible or ServiceState.RequestPending)
                restoredState = restoredState with
                {
                    Session = restoredState.Session with
                    {
                        Services = restoredState.Session.Services.SetItem(ServiceKind.Deboarding,
                    legacyService with { State = ServiceState.Skipped, LastEvidence = new(EvidenceSource.System, "M7 migration: pilot-requested removal of deboarding", clock.UtcNow), CompletionProvenance = CompletionProvenance.Unknown })
                    }
                };
            restoredState = restoredState with
            {
                SupportedServices = restoredState.SupportedServices.Remove(ServiceKind.Deboarding),
                CancellableServices = restoredState.CancellableServices.Remove(ServiceKind.Deboarding),
                TriggerableServices = restoredState.TriggerableServices?.Remove(ServiceKind.Deboarding),
                ServicePermissions = restoredState.ServicePermissions.Remove(ServiceKind.Deboarding),
                Question = restoredState.Question?.Service == ServiceKind.Deboarding ? null : restoredState.Question
            };
            saved = saved with { Checkpoint = saved.Checkpoint with { State = restoredState } };
            _runtime = new(saved.Checkpoint, conversation, clock); _flight = saved.Flight; _aircraft = saved.AircraftBinding;
            Apply(new ObserveGate(_runtime.Metadata(EvidenceSource.System), new(GateReadinessState.Unknown, GateProvenance.Unknown, null)));
            _attempted = await store.AttemptedAsync(saved.Checkpoint.State.Session.Id, ct).ConfigureAwait(false);
            _rejected = await store.RejectedAsync(saved.Checkpoint.State.Session.Id, ct).ConfigureAwait(false);
            _armed = false; Status = "Saved operation restored. Check the aircraft and current GSX states, then reconcile; nothing has been replayed.";
            await store.SaveAsync(Saved, ct).ConfigureAwait(false); return true;
        }
        finally { _gate.Release(); }
    }

    public async Task ReconcileAsync(SimulatorSnapshot simulator, bool reviewedCurrentFlight, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is null || !reviewedCurrentFlight) throw new InvalidOperationException("Confirm that current GSX service states belong to this operation.");
            FlightPlanValidation.Validate(_flight!, clock.UtcNow);
            ValidateEnvironment(simulator, live.Snapshot, _flight!, _aircraft);
            if (_duty is not null && _runtime.Checkpoint.State.Session.Stage != WorkflowStage.Completed)
                _duty = _duty with { State = DutyState.GroundHandling, RequiresLocationReview = false, Arrival = null };
            // Freeze the reducer while applying a full snapshot. No request can be planned from a partial reconciliation.
            Apply(new ChangeConnection(_runtime.Metadata(EvidenceSource.System), ConnectionState.ReconciliationRequired));
            Apply(new ObserveGate(_runtime.Metadata(EvidenceSource.System), new(GateReadinessState.Unknown, GateProvenance.Unknown, null)));
            _generation = live.Snapshot.Generation; _last.Clear(); _armed = true;
            await ReconcileCoreAsync(true, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ObserveAsync(SimulatorSnapshot simulator, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ObserveDutyAsync(simulator, ct).ConfigureAwait(false);
            if (_runtime is null) return;
            if (_runtime.Checkpoint.State.Session.Stage == WorkflowStage.Completed) return;
            var snapshot = live.Snapshot;
            try { ValidateEnvironment(simulator, snapshot, _flight!, _aircraft); }
            catch (InvalidOperationException exception) { await SuspendAsync(exception.Message, ct).ConfigureAwait(false); return; }
            if (snapshot.Generation != _generation) { await SuspendAsync("GSX reconnected. Review and reconcile before further requests.", ct).ConfigureAwait(false); return; }
            if (!_armed) return;
            Guid inputHoldId = new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"ADM/GSX-input/{_runtime.Checkpoint.State.Session.Id:D}")).AsSpan(0, 16));
            if (_runtime.Checkpoint.State.Session.Holds.FirstOrDefault(hold => hold.Id == inputHoldId)?.IsActive != snapshot.AwaitingInput &&
                (snapshot.AwaitingInput || _runtime.Checkpoint.State.Session.Holds.Any(hold => hold.Id == inputHoldId)))
            {
                Apply(new ChangeHold(_runtime.Metadata(EvidenceSource.Gsx, "GSX text-input prompt state"),
                    new(inputHoldId, HoldKind.RealServiceBlocked, "GSX needs an answer. Finish its open question in the simulator; I'll continue afterwards.", snapshot.AwaitingInput, null, null), Notify: false));
                await store.SaveAsync(Saved, ct).ConfigureAwait(false);
            }
            if (snapshot.AwaitingInput || snapshot.MenuOpen)
                foreach (var kind in _acceptedAt.Keys.ToArray()) _acceptedAt[kind] = clock.UtcNow;
            foreach (var (kind, acceptedAt) in _acceptedAt)
                if (!_engaged.Contains(kind) && clock.UtcNow - acceptedAt > TimeSpan.FromSeconds(20) &&
                    _runtime.Checkpoint.State.Session.Services[kind].State == ServiceState.Requested &&
                    (!snapshot.Services.TryGetValue(kind, out var pending) || pending.State is not (ServiceState.Requested or ServiceState.Active or ServiceState.Completed)))
                {
                    StalledService = kind;
                    await SuspendAsync($"GSX accepted {kind.ToString().ToLowerInvariant()} but hasn't reported it starting. Check GSX; restart only when the service is available again.", ct).ConfigureAwait(false); return;
                }
            if (snapshot.Services.TryGetValue(ServiceKind.Boarding, out var boarding) && boarding.State is ServiceState.Requested or ServiceState.Active &&
                _runtime.Checkpoint.State.Session.Services[ServiceKind.Boarding].State is not (ServiceState.Requested or ServiceState.Active))
            { await SuspendAsync("Boarding was started outside ADM. Automatic preboarding requests are paused.", ct).ConfigureAwait(false); return; }
            var provider = await gateway.ProbeAsync(ct).ConfigureAwait(false);
            if (!_runtime.Checkpoint.State.SupportedServices.IsSubsetOf(provider.RequestableServices))
            {
                await SuspendAsync("GSX service capabilities changed. Review the aircraft and reconcile before continuing.", ct).ConfigureAwait(false); return;
            }
            var available = snapshot.Services.Where(pair => pair.Value.CanTrigger && !pair.Value.Waiting).Select(pair => pair.Key).ToImmutableHashSet();
            bool changed = _runtime.Checkpoint.State.TriggerableServices is not { } previousAvailability || !previousAvailability.SetEquals(available);
            if (changed) Apply(new UpdateServiceAvailability(_runtime.Metadata(EvidenceSource.System), available));
            foreach (var (kind, status) in snapshot.Services)
            {
                if (kind == ServiceKind.Deboarding) continue;
                if (_last.TryGetValue(kind, out var prior) && prior == (status.State, status.Waiting)) continue;
                _last[kind] = (status.State, status.Waiting); changed = true;
                var current = _runtime.Checkpoint.State.Session.Services[kind];
                bool sent = HasUncertainOrAcceptedSend(kind);
                if (status.IsIdleUnavailable && !sent && current.State is ServiceState.Planned or ServiceState.Eligible or ServiceState.RequestPending)
                {
                    if (current.State == ServiceState.RequestPending) Apply(new ReconcileService(Meta(status.Evidence), kind, ServiceState.Planned));
                    Hold(kind, status); continue;
                }
                if (status.State is ServiceState.Requested or ServiceState.Active) _engaged.Add(kind);
                if (status.State == ServiceState.Planned && current.State == ServiceState.Active && _engaged.Contains(kind))
                {
                    if (kind is ServiceKind.Refuelling or ServiceKind.Boarding)
                        Apply(new RequestServiceCompletionCheck(Meta(status.Evidence), kind));
                    else if (!_runtime.Checkpoint.State.Session.Plan.Services[kind].Rule.Required)
                    {
                        var cycleEvidence = status.Evidence with { Reference = $"Observed current operation cycle: {current.LastEvidence?.Reference} -> {status.Evidence.Reference}; same GSX generation {_generation}" };
                        Apply(new ObserveServiceCycleEnd(Meta(cycleEvidence), kind));
                        diagnostic?.Invoke($"{kind} returned idle after active work; optional cycle resolved without claiming verified completion.");
                    }
                    else
                        Apply(new ReconcileService(Meta(status.Evidence), kind, ServiceState.Unknown));
                    Hold(kind, status); continue;
                }
                if (status.State == ServiceState.Planned && current.State is ServiceState.Requested or ServiceState.Completed)
                { await SuspendAsync("GSX service state reset. Reconciliation is required; completed work will not be requested again.", ct).ConfigureAwait(false); return; }
                if (status.State == ServiceState.Completed && (current.State is ServiceState.Planned or ServiceState.RequestPending) && !_engaged.Contains(kind)) continue;
                if (current.State == ServiceState.Unknown)
                {
                    // A late, authoritative completion of this uninterrupted checked cycle needs no manual answer.
                    if (status.State == ServiceState.Completed && _runtime.Checkpoint.State.ServiceCompletionChecks.Contains(kind))
                    { Apply(new ReconcileService(Meta(status.Evidence), kind, ServiceState.Completed)); Hold(kind, status); }
                    continue; // Other uncertain delivery still requires explicit reconciliation.
                }
                if (status.State is ServiceState.Requested or ServiceState.Active && current.State is ServiceState.Planned or ServiceState.NotRequired or ServiceState.Eligible or ServiceState.CycleEnded or ServiceState.Completed or ServiceState.Skipped or ServiceState.Cancelled)
                    Apply(new ReconcileService(Meta(status.Evidence), kind, status.State));
                else if (status.State == ServiceState.Completed && _engaged.Contains(kind))
                    Apply(new ReconcileService(Meta(status.Evidence), kind, status.State));
                else if (status.State != ServiceState.Planned && current.State != status.State)
                    Apply(new ObserveService(Meta(status.Evidence), kind, status.State));
                Hold(kind, status);
            }
            changed |= AdvanceRealism();
            if (changed) await store.SaveAsync(Saved, ct).ConfigureAwait(false);
            await DispatchAsync(simulator, ct).ConfigureAwait(false);
            RefreshStatus();
        }
        finally { _gate.Release(); }
    }

    public async Task SendAsync(string message, SimulatorSnapshot simulator, CancellationToken ct)
    {
        // Apply pending provider changes before interpreting a context-sensitive approval.
        await ObserveAsync(simulator, ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is null) return;
            var intent = conversation.Parse(message, _runtime.Context);
            if (intent.Kind is PilotIntentKind.ApproveServicesStart or PilotIntentKind.ApproveBoarding or PilotIntentKind.ApproveClosure or PilotIntentKind.ConfirmRelease or PilotIntentKind.ConfirmServiceCompletion or PilotIntentKind.SkipToService)
            {
                try { ValidateEnvironment(simulator, live.Snapshot, _flight!, _aircraft); }
                catch (InvalidOperationException exception) { await SuspendAsync(exception.Message, ct).ConfigureAwait(false); return; }
                if (!_armed || live.Snapshot.Generation != _generation)
                { await SuspendAsync("Review and reconcile the current connection before approving handling actions.", ct).ConfigureAwait(false); return; }
            }
            if (intent.Kind == PilotIntentKind.ResumeService && intent.Service is { } restarting &&
                _runtime.Checkpoint.State.Session.Services[restarting].State == ServiceState.Unknown &&
                (!_armed || !live.Snapshot.Services.TryGetValue(restarting, out var currentService) || !currentService.CanTrigger || currentService.Waiting))
                throw new InvalidOperationException("Reconcile first, then restart only when GSX shows this service available.");
            if (intent.Kind == PilotIntentKind.ResumeService && intent.Service is { } recovering &&
                _runtime.Checkpoint.State.Session.Services[recovering].State is ServiceState.Unknown or ServiceState.Failed &&
                gateway is IGsxRestartRecovery recovery &&
                (!_armed || !await recovery.PrepareRestartAsync(recovering, _generation, ct).ConfigureAwait(false)))
                throw new InvalidOperationException("GSX has not freshly confirmed this service is available. Recheck its state before restarting; the previous request was not repeated.");
            var previousService = intent.Service is { } selectedService ? _runtime.Checkpoint.State.Session.Services[selectedService] : null;
            _runtime.Send(message);
            if (intent.Kind == PilotIntentKind.ResumeService && intent.Service is { } resumed && previousService != _runtime.Checkpoint.State.Session.Services[resumed])
            {
                _engaged.Remove(resumed);
                _acceptedAt.Remove(resumed); StalledService = null;
                if (live.Snapshot.Services.TryGetValue(resumed, out var fresh)) _last[resumed] = (fresh.State, fresh.Waiting);
            }
            await store.SaveAsync(Saved, ct).ConfigureAwait(false);
            if (_armed) await DispatchAsync(simulator, ct).ConfigureAwait(false);
            RefreshStatus();
        }
        finally { _gate.Release(); }
    }
    public async Task ConfirmGateAsync(SimulatorSnapshot simulator, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is null) return;
            ValidateEnvironment(simulator, live.Snapshot, _flight!, _aircraft);
            var evidence = new Evidence(EvidenceSource.User, "Pilot checked the gate for this operation", clock.UtcNow);
            Apply(new ObserveGate(Meta(evidence), new(GateReadinessState.Ready, GateProvenance.PilotAttested, evidence)));
            await store.SaveAsync(Saved, ct).ConfigureAwait(false); RefreshStatus();
        }
        finally { _gate.Release(); }
    }
    public async Task EndAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is not null)
            {
                _duty = DutyTracking.Complete(_duty!, _runtime.Checkpoint.State.Session.Id, _runtime.Checkpoint.State.Session.Stage == WorkflowStage.Completed, true);
                await store.SaveAsync(Saved with { Archived = true }, ct).ConfigureAwait(false);
            }
            _armed = false; _runtime = null; _flight = null; _attempted = []; _rejected = []; _last.Clear(); _engaged.Clear();
            _acceptedAt.Clear(); StalledService = null;
            Status = "Operation archived. Existing GSX services were not cancelled or marked complete.";
        }
        finally { _gate.Release(); }
    }
    private async Task ReconcileCoreAsync(bool allowExistingCompletion, CancellationToken ct)
    {
        var capabilities = await gateway.ProbeAsync(ct).ConfigureAwait(false);
        Apply(new UpdateServiceAvailability(_runtime!.Metadata(EvidenceSource.System), live.Snapshot.Services.Where(pair => pair.Value.CanTrigger && !pair.Value.Waiting).Select(pair => pair.Key).ToImmutableHashSet()));
        Apply(new UpdateCapabilities(_runtime!.Metadata(EvidenceSource.System), _runtime.Checkpoint.State.SupportedServices.Intersect(capabilities.RequestableServices), [],
            _runtime.Checkpoint.State.ValidatedConcurrentGroups));
        foreach (var (kind, status) in live.Snapshot.Services)
        {
            if (kind == ServiceKind.Deboarding) continue;
            var current = _runtime!.Checkpoint.State.Session.Services[kind];
            bool sent = HasUncertainOrAcceptedSend(kind);
            if (_duty is { Sectors.Length: > 1 } && !sent && status.State is ServiceState.Completed or ServiceState.Active or ServiceState.Requested)
            { Apply(new ReconcileService(Meta(status.Evidence), kind, current.State == ServiceState.NotRequired ? ServiceState.NotRequired : ServiceState.Unknown)); continue; }
            if (_duty is { Sectors.Length: > 1 } && !sent && current.State == ServiceState.Unknown && status.State == ServiceState.Planned && status.CanTrigger)
            { Apply(new ReconcileService(Meta(status.Evidence), kind, ServiceState.Planned)); Hold(kind, status); continue; }
            if (status.IsIdleUnavailable && !sent && current.State is ServiceState.Planned or ServiceState.Eligible or ServiceState.RequestPending or ServiceState.Unknown)
            { Apply(new ReconcileService(Meta(status.Evidence), kind, ServiceState.Planned)); Hold(kind, status); continue; }
            if (status.State is ServiceState.Requested or ServiceState.Active) _engaged.Add(kind);
            if (status.State == ServiceState.Completed && !allowExistingCompletion && current.State != ServiceState.NotRequired ||
                status.State == ServiceState.Planned && sent && current.State == ServiceState.Unknown)
            { Apply(new ReconcileService(Meta(status.Evidence), kind, ServiceState.Unknown)); continue; }
            if (current.State == ServiceState.NotRequired && status.State is ServiceState.Completed or ServiceState.Planned or ServiceState.Unknown) continue;
            if (status.State == ServiceState.Planned && current.State == ServiceState.Completed)
            { Apply(new ReconcileService(Meta(status.Evidence), kind, ServiceState.Unknown)); continue; }
            if (status.State == ServiceState.Planned && current.State is ServiceState.Skipped or ServiceState.Cancelled or ServiceState.CycleEnded) continue;
            Apply(new ReconcileService(Meta(status.Evidence), kind, status.State));
            Hold(kind, status);
            _last[kind] = (status.State, status.Waiting);
        }
        // Lost journaled requests with no current provider entry stay Unknown rather than being replayed.
        AdvanceRealism();
        Apply(new ChangeConnection(_runtime!.Metadata(EvidenceSource.System), ConnectionState.Connected));
        await store.SaveAsync(Saved, ct).ConfigureAwait(false);
        RefreshStatus();
    }
    private void Hold(ServiceKind kind, GsxServiceStatus status)
    {
        if (kind == ServiceKind.Boarding) return;
        var id = _runtime!.Checkpoint.State.Session.Services[kind].Id;
        var existing = _runtime.Checkpoint.State.Session.Holds.FirstOrDefault(hold => hold.Id == id);
        if (existing?.IsActive == status.Waiting || existing is null && !status.Waiting) return;
        Apply(new ChangeHold(Meta(status.Evidence), new(id, HoldKind.RealServiceBlocked,
            $"GSX reports {kind.ToString().ToLowerInvariant()} waiting for another vehicle or service.", status.Waiting, null, null), Notify: false));
    }
    private bool HasUncertainOrAcceptedSend(ServiceKind kind) => _runtime!.Checkpoint.State.Commands.Any(command => command.Service == kind && _attempted.Contains(command.Id) && !_rejected.Contains(command.Id));
    private async Task DispatchAsync(SimulatorSnapshot simulator, CancellationToken ct)
    {
        if (!_armed || live.Snapshot.AwaitingInput || live.Snapshot.MenuOpen) return;
        if (_runtime!.Checkpoint.State.Session.Stage == WorkflowStage.Completed) { _armed = false; return; }
        ValidateEnvironment(simulator, live.Snapshot, _flight!, _aircraft);
        foreach (var command in _runtime!.Checkpoint.State.Commands.Where(command => command.Kind == OperationalCommandKind.RequestService).ToArray())
        {
            try { ValidateEnvironment(latestSimulator?.Invoke() ?? simulator, live.Snapshot, _flight!, _aircraft); }
            catch (InvalidOperationException exception) { await SuspendAsync(exception.Message, ct).ConfigureAwait(false); return; }
            if (live.Snapshot.Generation != _generation) { await SuspendAsync("GSX connection changed before dispatch. Reconcile before continuing.", ct).ConfigureAwait(false); return; }
            if (_attempted.Contains(command.Id) || !OperationalCommandPolicy.CanDispatch(_runtime.Checkpoint.State, command)) continue;
            if (command.Service == ServiceKind.Boarding && live.Snapshot.Services.Values.Any(service => service.Waiting)) continue;
            if (!live.Snapshot.Services.TryGetValue(command.Service!.Value, out var status) || !status.CanTrigger || status.Waiting) continue;
            if (command.Service == ServiceKind.Catering && _runtime.Checkpoint.State.RequestFuelAndCateringTogether &&
                _runtime.Checkpoint.State.Session.Plan.ServicePhases[ServiceKind.Catering] == _runtime.Checkpoint.State.Session.Plan.ServicePhases[ServiceKind.Refuelling] &&
                _runtime.Checkpoint.State.Session.Services[ServiceKind.Refuelling].State is ServiceState.Planned or ServiceState.Eligible or ServiceState.RequestPending or ServiceState.Requested &&
                (!live.Snapshot.Services.TryGetValue(ServiceKind.Refuelling, out var fuel) || fuel.State is not (ServiceState.Requested or ServiceState.Active or ServiceState.Completed))) continue;
            if (!await store.TryBeginSendAsync(Saved, command, ct).ConfigureAwait(false)) { _attempted = _attempted.Add(command.Id); continue; }
            _attempted = _attempted.Add(command.Id);
            CommandReceipt receipt;
            try
            {
                receipt = await gateway.RequestServiceAsync(new(command.Id, command.SessionId, command.Service.Value,
                    command.IntentVersion, command.PlanSnapshotId, command.PolicySource), ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or InvalidOperationException)
            { receipt = new(command.Id, ReceiptStatus.Unknown, "Delivery uncertain. Reconcile before continuing."); }
            if (receipt.CommandId != command.Id || !Enum.IsDefined(receipt.Status)) receipt = new(command.Id, ReceiptStatus.Unknown, "Unmatched or invalid receipt. Reconciliation is required.");
            Apply(new AcknowledgeCommand(_runtime.Metadata(EvidenceSource.System), command.Id, receipt.Status switch
            { ReceiptStatus.Accepted => CommandOutcome.Accepted, ReceiptStatus.Rejected => CommandOutcome.Rejected, _ => CommandOutcome.Unknown }));
            AdvanceRealism();
            await store.RecordReceiptAsync(Saved, command.Id, receipt.Status, CancellationToken.None).ConfigureAwait(false);
            if (receipt.Status == ReceiptStatus.Accepted) _acceptedAt[command.Service.Value] = clock.UtcNow;
            if (receipt.Status == ReceiptStatus.Rejected) _rejected = _rejected.Add(command.Id);
            if (receipt.Status != ReceiptStatus.Accepted) { _armed = false; Status = receipt.Detail; return; }
        }
    }
    private async Task SuspendAsync(string reason, CancellationToken ct)
    {
        if (_armed || _runtime!.Checkpoint.State.Connection == ConnectionState.Connected)
        {
            diagnostic?.Invoke("Handling paused: " + reason);
            _armed = false; Apply(new ChangeConnection(_runtime!.Metadata(EvidenceSource.System), ConnectionState.ReconciliationRequired));
            await store.SaveAsync(Saved, ct).ConfigureAwait(false);
        }
        Status = reason;
    }
    private bool AdvanceRealism()
    {
        if (realismEngine is null || _runtime?.Checkpoint.State.Realism is not { } current || _runtime.Checkpoint.State.Session.Stage == WorkflowStage.Completed) return false;
        var next = realismEngine.Advance(_runtime.Checkpoint.State, clock.UtcNow);
        if (next == current) return false;
        Apply(new UpdateOperationalRealism(_runtime.Metadata(EvidenceSource.Simulation, "Local seeded operational simulation"), next));
        return true;
    }
    private void RefreshStatus() => Status = live.Snapshot.AwaitingInput ? "GSX has a text field open. Complete or close it in the simulator." : live.Snapshot.MenuOpen ? "GSX menu is open. Close it to continue service requests." : _runtime!.Checkpoint.State.Session.Stage switch
    {
        WorkflowStage.Planning when _runtime.Checkpoint.State.RequiresServiceStartApproval && _runtime.Checkpoint.State.ServiceStartApproval is null => "Ready when you are. Reply in Chat to start services.",
        WorkflowStage.AwaitingBoardingApproval => "Ready for your boarding go-ahead. This also confirms the gate is ready.",
        WorkflowStage.Boarding => "Boarding requested or in progress. Waiting for GSX completion evidence.",
        WorkflowStage.AwaitingClosureApproval => "Boarding is complete. Waiting for your approval to close up and disconnect.",
        WorkflowStage.ClosingAndReleasing => "Finishing ADM's session. Doors and equipment remain with the pilot and GSX.",
        WorkflowStage.Completed => "Ground handling complete. Pushback is entirely yours; ADM continues monitoring the simulator.",
        WorkflowStage.ArrivalHandling or WorkflowStage.PreBoardServices => "Ground services in progress. You can ask for an update at any time.",
        _ => OperationalReducer.DelayDescription(_runtime.Checkpoint.State)
    };
    private EventMetadata Meta(Evidence evidence) => _runtime!.Metadata(evidence.Source, evidence.Reference) with { Evidence = evidence };
    private void Apply(OperationEvent input)
    {
        _runtime!.Accept(input);
        if (_runtime.Checkpoint.State.Session.Stage == WorkflowStage.Completed && _duty is not null)
            _duty = DutyTracking.Complete(_duty, _runtime.Checkpoint.State.Session.Id, true, true);
    }
    private async Task ObserveDutyAsync(SimulatorSnapshot sim, CancellationToken ct)
    {
        if (_duty is null || _duty.State == DutyState.Ended || sim.Source != EvidenceSource.SimConnect || sim.Connection != ConnectionState.Connected ||
            clock.UtcNow - sim.ObservedAt > TimeSpan.FromSeconds(5) || sim.ObservedAt - clock.UtcNow > TimeSpan.FromSeconds(2) ||
            sim.OnGround is null || sim.GroundSpeedKnots is null || string.IsNullOrWhiteSpace(sim.AircraftTitle)) return;
        var gsx = live.Snapshot;
        bool gsxCurrent = gsx.Connection == ConnectionState.Connected && clock.UtcNow - gsx.ReceivedAt < TimeSpan.FromSeconds(10);
        string? airport = FlightPlanValidation.Airport(sim.AirportIcao) ? sim.AirportIcao : gsxCurrent && FlightPlanValidation.Airport(gsx.AirportIcao) ? gsx.AirportIcao : null;
        var prior = _duty;
        _duty = DutyTracking.Observe(_duty, new(sim.ObservedAt, sim.OnGround.Value, sim.GroundSpeedKnots.Value,
            gsxCurrent && !string.IsNullOrWhiteSpace(gsx.Stand), sim.AircraftTitle, airport, sim.Stand ?? (gsxCurrent ? gsx.Stand : null), sim.Latitude, sim.Longitude));
        if (prior.State != _duty.State && _duty.State == DutyState.ArrivalCandidate)
            Status = "Arrival candidate noted. Import or enter the next plan in Flight, confirm the actual airport, then prepare the next turnaround or end duty.";
        if (_duty.DepartureObserved && !prior.DepartureObserved && _runtime is not null)
        {
            _armed = false; _runtime.ClearChat();
            _duty = DutyTracking.Complete(_duty, _runtime.Checkpoint.State.Session.Id, _runtime.Checkpoint.State.Session.Stage == WorkflowStage.Completed, true);
            await store.SaveAsync(Saved with { Archived = true }, ct).ConfigureAwait(false);
            _runtime = null; _flight = null; _last.Clear(); _engaged.Clear(); _attempted = []; _rejected = [];
            Status = "Airborne. Chat cleared and handling archived; monitoring the arrival.";
        }
        if (prior.State != _duty.State || prior.Arrival != _duty.Arrival || clock.UtcNow - _dutySavedAt > TimeSpan.FromSeconds(30))
        {
            if (store is IDutyStore duties) await duties.SaveDutyAsync(_duty, ct).ConfigureAwait(false);
            _dutySavedAt = clock.UtcNow;
        }
    }
    public static void ValidateEnvironment(SimulatorSnapshot sim, GsxLiveSnapshot gsx, FlightPlanSnapshot flight, string? aircraftBinding)
    {
        if (sim.Source != EvidenceSource.SimConnect || sim.Connection != ConnectionState.Connected || DateTimeOffset.UtcNow - sim.ObservedAt > TimeSpan.FromSeconds(5) ||
            sim.ObservedAt > DateTimeOffset.UtcNow.AddSeconds(2) ||
            string.IsNullOrWhiteSpace(sim.AircraftTitle) || gsx.Connection != ConnectionState.Connected)
            throw new InvalidOperationException("Live handling requires a current GSX connection and simulator aircraft identity.");
        if (aircraftBinding is not null && sim.AircraftTitle != aircraftBinding)
            throw new InvalidOperationException("Simulator aircraft changed. The active operation is paused.");
        if (gsx.AirportIcao is { Length: > 0 } airport && airport != flight.Origin)
            throw new InvalidOperationException("GSX airport differs from this operation's origin. Review the imported flight.");
    }
}
