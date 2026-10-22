using System.Collections.Immutable;

namespace ADM.Domain;

/// <summary>Pure reducer. Effects are journaled intent only; this class never performs I/O.</summary>
public static class OperationalReducer
{
    public static Reduction Apply(OperationalState initial, OperationEvent input)
    {
        var metadata = input.Metadata;
        if (metadata.SessionId != initial.Session.Id || metadata.EventId == Guid.Empty || initial.SeenEvents.Contains(metadata.EventId))
            return new(initial, [], []);
        var state = initial with { SeenEvents = initial.SeenEvents.Add(metadata.EventId) };
        var notices = ImmutableArray.CreateBuilder<WorkflowNotice>();
        var commands = ImmutableArray.CreateBuilder<OperationalCommand>();
        if (state.Session.Stage == WorkflowStage.Completed)
        {
            if (input is PilotInstruction { Directive: PilotDirective.Status or PilotDirective.Delay }) Say("Ground handling is complete. Pushback remains with you.");
            return Result();
        }

        switch (input)
        {
            case UpdateOperationalRealism update when metadata.Source == EvidenceSource.Simulation:
                if (update.Progress.Incident is { } scopedIncident && scopedIncident.SessionId != state.Session.Id) { Say("The incident belongs to a different operation."); break; }
                if (state.Realism is not { } previousRealism || update.Progress.Options != previousRealism.Options || update.Progress.GateProfile != previousRealism.GateProfile ||
                    update.Progress.PreparedAt != previousRealism.PreparedAt || previousRealism.DecisionMade && !update.Progress.DecisionMade ||
                    previousRealism.DecisionMade && previousRealism.Incident is null && update.Progress.Incident is not null ||
                    previousRealism.Incident is { } previousIncident && (update.Progress.Incident is not { } nextIncident || previousIncident.Id != nextIncident.Id ||
                        previousIncident.Kind != nextIncident.Kind || previousIncident.StartedAt != nextIncident.StartedAt || previousIncident.ExpectedResolution != nextIncident.ExpectedResolution ||
                        previousIncident.Status != DisruptionStatus.Active && nextIncident.Status != previousIncident.Status))
                { Say("The saved realism decision cannot be replaced or sampled again."); break; }
                update.Progress.Validate();
                ApplyRealism(update.Progress);
                if (previousRealism.Incident is null && update.Progress.Incident is { } started)
                    notices.Add(new($"disruption.{IncidentKey(started.Kind)}:{started.Id}", $"{OperationalRealismState.Description(started.Kind)}. Estimated hold: {(int)(started.ExpectedResolution - started.StartedAt).TotalMinutes} minutes.", true));
                if (previousRealism.Incident is { Status: DisruptionStatus.Active } && update.Progress.Incident?.Status == DisruptionStatus.Resolved)
                    notices.Add(new($"disruption.resolved:{update.Progress.Incident.Id}", "The simulated delay has cleared.", true));
                break;
            case ObserveServiceCycleEnd ended:
                if (metadata.Source != EvidenceSource.Gsx || metadata.Evidence?.Source != EvidenceSource.Gsx || !EvidenceValid(metadata.Evidence) ||
                    !state.Session.Services.TryGetValue(ended.Kind, out var endedService) || endedService.State != ServiceState.Active ||
                    ended.Kind is ServiceKind.Refuelling or ServiceKind.Boarding || state.Session.Plan.Services[ended.Kind].Rule.Required) break;
                Invalidate(); SetService(ended.Kind, ServiceState.CycleEnded, metadata.Evidence);
                break;
            case RequestServiceCompletionCheck check:
                if (metadata.Source != EvidenceSource.Gsx || metadata.Evidence?.Source != EvidenceSource.Gsx || !EvidenceValid(metadata.Evidence) ||
                    check.Kind is not (ServiceKind.Refuelling or ServiceKind.Boarding) || state.Session.Services[check.Kind].State is not (ServiceState.Active or ServiceState.Requested)) break;
                Invalidate();
                SetService(check.Kind, ServiceState.Unknown, metadata.Evidence);
                state = state with { ServiceCompletionChecks = state.ServiceCompletionChecks.Add(check.Kind) };
                break;
            case UpdateServiceAvailability availability when metadata.Source == EvidenceSource.System:
                if (!availability.Triggerable.Contains(ServiceKind.Boarding) && (state.Question?.Kind == QuestionKind.Boarding ||
                    state.Session.Services[ServiceKind.Boarding].State == ServiceState.RequestPending)) Invalidate();
                state = state with { TriggerableServices = availability.Triggerable };
                break;
            case UpdateCapabilities capabilities when metadata.Source == EvidenceSource.System:
                state = state with
                {
                    SupportedServices = capabilities.Services,
                    CancellableServices = capabilities.Cancellations,
                    ValidatedConcurrentGroups = capabilities.ConcurrentGroups
                };
                break;
            case ReconcileService reconcile:
                if (metadata.Source != EvidenceSource.Gsx || !EvidenceValid(metadata.Evidence) || metadata.Evidence!.Source != EvidenceSource.Gsx ||
                    !state.Session.Services.TryGetValue(reconcile.Kind, out var reconciled) ||
                    reconcile.State is not (ServiceState.Planned or ServiceState.Requested or ServiceState.Active or ServiceState.Completed or ServiceState.Unknown) ||
                    reconciled.State == ServiceState.NotRequired && reconcile.State == ServiceState.Completed)
                { Say("Reconciliation needs current GSX evidence and a valid service state."); break; }
                Invalidate(); SetService(reconcile.Kind, reconcile.State, metadata.Evidence);
                if (reconcile.State is ServiceState.Completed or ServiceState.Active or ServiceState.Requested)
                    state = state with { ServiceCompletionChecks = state.ServiceCompletionChecks.Remove(reconcile.Kind) };
                break;
            case ObserveService observation:
                Observe(observation);
                break;
            case AcknowledgeCommand acknowledgement:
                Acknowledge(acknowledgement);
                break;
            case ObserveGate observation:
                if (observation.Gate.Evidence is { } gateEvidence && gateEvidence.Source != metadata.Source)
                { Say("Gate evidence source does not match the observation source."); break; }
                if (observation.Gate != state.Session.Gate)
                {
                    try { GateRules.Apply(observation.Gate); }
                    catch (ArgumentException exception) { Say(exception.Message); break; }
                    if (state.Realism is { Incident.Status: DisruptionStatus.Active } realism && observation.Gate.State == GateReadinessState.Ready)
                    { state = state with { Realism = realism with { GateBeforeHold = observation.Gate } }; break; }
                    if (observation.Gate.State != state.Session.Gate.State || observation.Gate.Provenance != state.Session.Gate.Provenance) Invalidate();
                    state = state with { Session = state.Session with { Gate = observation.Gate } };
                }
                break;
            case ChangeHold change:
                if (!Enum.IsDefined(change.Hold.Kind) || !Enum.IsDefined(change.Hold.Scope)) { Say("Unknown hold type or scope."); break; }
                if (change.Hold.IsSimulated && metadata.Source != EvidenceSource.Simulation ||
                    change.Hold.Kind == HoldKind.RealServiceBlocked && (metadata.Source is not (EvidenceSource.Gsx or EvidenceSource.SimConnect) || !EvidenceValid(metadata.Evidence)) ||
                    change.Hold.Kind == HoldKind.ManualPilotHold && metadata.Source != EvidenceSource.User)
                { Say("The hold change is not authorized for this evidence source."); break; }
                var existing = state.Session.Holds.FirstOrDefault(hold => hold.Id == change.Hold.Id);
                if (existing == change.Hold) break;
                if (change.Hold.Id == Guid.Empty || existing is not null && existing.Kind != change.Hold.Kind)
                { Say("A hold's origin cannot be changed."); break; }
                if (existing is not null && existing.Kind == change.Hold.Kind && existing.IsActive == change.Hold.IsActive && existing.Reason == change.Hold.Reason && existing.Scope == change.Hold.Scope)
                { state = state with { Session = state.Session with { Holds = state.Session.Holds.Replace(existing, change.Hold) } }; break; }
                Invalidate();
                var holds = state.Session.Holds.Where(hold => hold.Id != change.Hold.Id).Append(change.Hold).ToImmutableArray();
                state = state with { Session = state.Session with { Holds = holds } };
                if (change.Hold.IsActive && change.Hold.Kind is HoldKind.SimulatedGate or HoldKind.SimulatedStaff)
                    state = state with { Session = state.Session with { Gate = state.Session.Gate with { State = GateReadinessState.Held } } };
                else if (state.Session.Gate.State == GateReadinessState.Held && !holds.Any(hold => hold.IsActive && hold.Kind is HoldKind.SimulatedGate or HoldKind.SimulatedStaff))
                    state = state with { Session = state.Session with { Gate = new(GateReadinessState.Unknown, GateProvenance.Unknown, null) } };
                if (change.Notify && change.Hold.IsActive) notices.Add(new($"hold:{change.Hold.Id}:{state.Session.PrerequisiteRevision}", change.Hold.Reason, true));
                break;
            case ChangeConnection change:
                if (change.Connection == state.Connection) break;
                var interruptedServices = state.Session.Services.Values.Where(service => service.State is ServiceState.RequestPending or ServiceState.Requested or ServiceState.Active or ServiceState.CancelPending).ToArray();
                Invalidate();
                state = state with { Connection = change.Connection, FuelAttested = false, FuelAttestation = null };
                if (change.Connection != ConnectionState.Connected)
                {
                    if (state.Realism is { } realism) state = state with { Realism = realism with { GateBeforeHold = new(GateReadinessState.Unknown, GateProvenance.Unknown, null) } };
                    state = state with
                    {
                        Releases = state.Releases.ToImmutableDictionary(pair => pair.Key,
                        pair => pair.Value with { IsSatisfied = false, Evidence = null, Provenance = CompletionProvenance.Unknown })
                    };
                    foreach (var service in interruptedServices)
                        SetService(service.Kind, ServiceState.Unknown, null);
                    state = state with { Connection = ConnectionState.ReconciliationRequired };
                }
                break;
            case ObserveRelease release:
                if (state.Session.Stage != WorkflowStage.ClosingAndReleasing || !HasApproval(ApprovalKind.Closure) ||
                    !EvidenceValid(metadata.Evidence) || metadata.Evidence!.Source != metadata.Source || !state.Releases.ContainsKey(release.Action))
                { Say("Release is not approved or lacks reliable evidence."); break; }
                state = state with
                {
                    Releases = state.Releases.SetItem(release.Action,
                    new(release.Action.ToString(), true, metadata.Evidence, Provenance(metadata.Evidence!)))
                };
                state = state with { Question = null };
                break;
            case PilotInstruction instruction:
                HandlePilot(instruction);
                break;
        }
        Schedule();
        return Result();

        Reduction Result()
        {
            var filtered = notices.Any(notice => notice.Key.StartsWith("boarding.approval", StringComparison.Ordinal))
                ? notices.Where(notice => !notice.Key.StartsWith("disruption.resolved", StringComparison.Ordinal)).ToImmutableArray() : notices.ToImmutable();
            return new(state, filtered, commands.ToImmutable());
        }
        static string IncidentKey(DisruptionKind kind) => kind switch { DisruptionKind.GateDocuments => "gate-documents", DisruptionKind.GateStaff => "gate-staff", _ => "passenger-processing" };
        void ApplyRealism(OperationalRealismState progress)
        {
            var previous = state.Realism!;
            var ownedIds = (previous.Incident?.Effects ?? []).Select(effect => effect.Id).ToHashSet();
            var holds = state.Session.Holds.Where(hold => !ownedIds.Contains(hold.Id)).ToImmutableArray();
            if (progress.Incident is { } incident)
                holds = holds.AddRange(incident.Effects.Where(effect => effect.Kind != DisruptionEffectKind.ReadyTimeEstimate).Select(effect =>
                    new OperationalHold(effect.Id, incident.Kind switch { DisruptionKind.GateStaff => HoldKind.SimulatedStaff, DisruptionKind.PassengerProcessing => HoldKind.SimulatedPassenger, _ => HoldKind.SimulatedGate },
                        OperationalRealismState.Description(incident.Kind), effect.IsActive, incident.ExpectedResolution, incident.Id, HoldScope.Boarding)));
            var gate = state.Session.Gate;
            if (progress.Incident is { Status: DisruptionStatus.Active })
                gate = new(GateReadinessState.Held, GateProvenance.Simulation, new(EvidenceSource.Simulation, "Local linked gate/boarding incident", metadata.OccurredAt));
            else
            {
                if (previous.Incident is { Status: DisruptionStatus.Active }) gate = progress.GateBeforeHold ?? new(GateReadinessState.Unknown, GateProvenance.Unknown, null);
                if (progress.Options.Enabled && progress.GateProfile.SimulatedReadiness && gate.Provenance is not (GateProvenance.PilotAttested or GateProvenance.VerifiedExternal))
                    gate = new(progress.GatePrepared ? GateReadinessState.Ready : GateReadinessState.Preparing, GateProvenance.Simulation,
                        new(EvidenceSource.Simulation, $"Local airport gate rule {progress.GateProfile.Icao}", metadata.OccurredAt));
            }
            bool changed = gate.State != state.Session.Gate.State || gate.Provenance != state.Session.Gate.Provenance ||
                !holds.Where(hold => hold.IsActive).Select(hold => hold.Id).ToHashSet().SetEquals(state.Session.Holds.Where(hold => hold.IsActive).Select(hold => hold.Id));
            if (changed)
            {
                Invalidate(preserveServicePermissions: true);
            }
            state = state with { Realism = progress, Session = state.Session with { Gate = gate, Holds = holds } };
        }
        void Say(string text) => notices.Add(new($"reply:{metadata.EventId}", text));
        void SetStage(WorkflowStage stage) => state = state with { Session = state.Session with { Stage = stage } };
        void SetService(ServiceKind kind, ServiceState next, Evidence? evidence)
        {
            var service = state.Session.Services[kind] with
            {
                State = next,
                LastEvidence = evidence,
                CompletionProvenance = next == ServiceState.Completed && evidence is not null ? Provenance(evidence) : CompletionProvenance.Unknown
            };
            state = state with { Session = state.Session with { Services = state.Session.Services.SetItem(kind, service) } };
        }
        void Invalidate(bool preserveServicePermissions = false)
        {
            // Unsent boarding clearance cannot survive a material prerequisite change.
            if (state.Session.Services[ServiceKind.Boarding].State == ServiceState.RequestPending)
                SetService(ServiceKind.Boarding, ServiceState.Planned, null);
            if (!preserveServicePermissions)
                foreach (var pending in state.Session.Services.Values.Where(service => service.State == ServiceState.RequestPending && state.Session.Plan.Services[service.Kind].Rule.Policy == ServicePolicy.Ask).ToArray())
                    SetService(pending.Kind, ServiceState.Planned, null);
            state = state with
            {
                Question = null,
                ServicePermissions = preserveServicePermissions ? state.ServicePermissions : [],
                ServicePermissionEvidence = preserveServicePermissions ? state.ServicePermissionEvidence : ImmutableDictionary<ServiceKind, Evidence>.Empty,
                Session = state.Session with
                {
                    PrerequisiteRevision = state.Session.PrerequisiteRevision + 1,
                    Approvals = state.Session.Approvals.Select(approval => approval with { IsInvalidated = true }).ToImmutableArray()
                }
            };
        }
        bool HasApproval(ApprovalKind kind) => state.Session.Approvals.Any(approval => ApprovalValidity.IsValid(approval,
            state.Session.Id, kind, state.Session.PrerequisiteRevision));
        bool Held() => state.Session.Holds.Any(hold => hold.IsActive);
        bool Resolved(ServiceKind kind)
        {
            var service = state.Session.Services[kind];
            if (service.State is ServiceState.Completed or ServiceState.NotRequired) return true;
            if (service.State == ServiceState.CycleEnded) return kind is not (ServiceKind.Refuelling or ServiceKind.Boarding) && !state.Session.Plan.Services[kind].Rule.Required;
            if (service.State == ServiceState.Cancelled) return !state.Session.Plan.Services[kind].Rule.Required;
            if (service.State == ServiceState.Skipped)
                return !state.Session.Plan.Services[kind].Rule.Required || kind != ServiceKind.Boarding &&
                    (kind != ServiceKind.Refuelling || state.FuelAttested && state.FuelAttestation?.Source == EvidenceSource.User && EvidenceValid(state.FuelAttestation));
            return false;
        }
        bool ReadyForBoarding() => state.Connection == ConnectionState.Connected && !Held() &&
            (state.TriggerableServices is null || state.TriggerableServices.Contains(ServiceKind.Boarding)) &&
            state.Session.Gate.State == GateReadinessState.Ready && GateRules.HasEvidence(state.Session.Gate) &&
            Enum.GetValues<ServiceKind>().Where(kind => kind != ServiceKind.Boarding).All(Resolved);
        void Ask(QuestionKind kind, string key, string? text = null, ServiceKind? service = null, ReleaseAction? release = null)
        {
            var question = new PendingQuestion(kind, state.Session.PrerequisiteRevision, service, release);
            if (state.Question == question) return;
            state = state with { Question = question };
            notices.Add(new($"{key}:{state.Session.PrerequisiteRevision}:{service}:{release}", text, true));
        }
        void Queue(OperationalCommandKind kind, ServiceKind? service = null, ReleaseAction? release = null)
        {
            if (kind == OperationalCommandKind.RequestService && state.Session.Services[service!.Value].State is ServiceState.RequestPending or ServiceState.Requested or ServiceState.Active ||
                kind == OperationalCommandKind.CancelService && state.Session.Services[service!.Value].State == ServiceState.CancelPending ||
                kind == OperationalCommandKind.ReleaseEquipment && state.Commands.Any(command => command.Release == release)) return;
            int version = state.Commands.Count(command => command.Service == service && command.Release == release) + 1;
            var command = new OperationalCommand(OperationalFactory.StableId(metadata.EventId, commands.Count + 1), state.Session.Id,
                kind, service, release, version, state.Session.Plan.SnapshotId,
                service is { } serviceKind ? state.PilotOverrides.Contains(serviceKind) ? "CurrentFlightPilotOverride" : $"{state.Session.Plan.Services[serviceKind].ProfileKey}/{state.Session.Plan.Services[serviceKind].ProfileRevision}/{state.Session.Plan.Services[serviceKind].Source}" : "PilotClosureApproval",
                service is { } previousKind ? state.Session.Services[previousKind].State : ServiceState.NotRequired)
            { PrerequisiteRevision = state.Session.PrerequisiteRevision };
            state = state with { Commands = state.Commands.Add(command) };
            commands.Add(command);
            if (service is { } requestedKind) SetService(requestedKind,
                kind == OperationalCommandKind.CancelService ? ServiceState.CancelPending : ServiceState.RequestPending, null);
        }
        void Observe(ObserveService observation)
        {
            if (!state.Session.Services.TryGetValue(observation.Kind, out var current) || !EvidenceValid(metadata.Evidence) || metadata.Evidence!.Source != metadata.Source)
            { Say("Service observation lacks reliable evidence."); return; }
            if (observation.State == current.State) return;
            if (observation.State == ServiceState.Active && current.State == ServiceState.RequestPending)
                SetService(observation.Kind, ServiceState.Requested, metadata.Evidence);
            if (observation.State == ServiceState.Completed && state.Session.Services[observation.Kind].State == ServiceState.Requested)
                SetService(observation.Kind, ServiceState.Active, metadata.Evidence);
            var result = ServiceTransitions.Apply(state.Session.Services[observation.Kind].State, observation.State, metadata.Evidence);
            if (!result.Allowed) { Say(result.Reason!); return; }
            if (observation.State is ServiceState.Failed or ServiceState.Unknown or ServiceState.Cancelled) Invalidate();
            SetService(observation.Kind, observation.State, metadata.Evidence);
        }
        void Acknowledge(AcknowledgeCommand acknowledgement)
        {
            if (metadata.Source == EvidenceSource.User || !Enum.IsDefined(acknowledgement.Outcome)) { Say("Command receipts must come from a provider or the system."); return; }
            var command = state.Commands.FirstOrDefault(command => command.Id == acknowledgement.CommandId);
            if (command is null || state.AcknowledgedCommands.Contains(command.Id)) return;
            state = state with { AcknowledgedCommands = state.AcknowledgedCommands.Add(command.Id) };
            if (state.Commands.Last(known => known.Kind == command.Kind && known.Service == command.Service && known.Release == command.Release).Id != command.Id) return;
            if (acknowledgement.Outcome == CommandOutcome.Unknown)
            {
                Invalidate();
                state = state with { Connection = ConnectionState.ReconciliationRequired };
                if (command.Service is { } unknownKind) SetService(unknownKind, ServiceState.Unknown, null);
                return;
            }
            if (command.Service is not { } kind)
            {
                if (acknowledgement.Outcome == CommandOutcome.Rejected && command.Release is { } release)
                { Invalidate(); state = state with { SupportedReleaseActions = state.SupportedReleaseActions.Remove(release) }; Say("Equipment action was rejected. Fresh closure approval and manual confirmation are required."); }
                return;
            }
            var current = state.Session.Services[kind].State;
            if (acknowledgement.Outcome == CommandOutcome.Accepted && command.Kind == OperationalCommandKind.RequestService && kind != ServiceKind.Boarding &&
                state.RequiresServiceStartApproval && state.ServiceStartApproval is { Source: EvidenceSource.User })
                notices.Add(new("services.starting", "Services starting. I'll keep you posted when you need to do something.", true));
            if (acknowledgement.Outcome == CommandOutcome.Accepted && command.Kind == OperationalCommandKind.RequestService && kind != ServiceKind.Boarding)
                state = state with { ServicesHaveStarted = true };
            if (command.Kind == OperationalCommandKind.RequestService && current == ServiceState.RequestPending)
                SetService(kind, acknowledgement.Outcome == CommandOutcome.Accepted ? ServiceState.Requested : ServiceState.Failed, null);
            if (command.Kind == OperationalCommandKind.CancelService && current == ServiceState.CancelPending && acknowledgement.Outcome == CommandOutcome.Rejected)
            { SetService(kind, command.PreviousState, null); Say("Cancellation was rejected. Use the GSX controls manually; cancellation is not confirmed."); }
            if (acknowledgement.Outcome == CommandOutcome.Rejected) Invalidate();
        }
        void HandlePilot(PilotInstruction instruction)
        {
            if (metadata.Source != EvidenceSource.User) { Say("Pilot permission must come from the user."); return; }
            var kind = instruction.Service;
            if (kind is { } focus && state.Session.Services.ContainsKey(focus)) state = state with { FocusService = focus };
            switch (instruction.Directive)
            {
                case PilotDirective.ApproveServicesStart:
                    if (!state.RequiresServiceStartApproval || state.ServiceStartApproval is not null || state.Connection != ConnectionState.Connected ||
                        state.Question?.Kind != QuestionKind.ServicesStart || state.Question.Revision != state.Session.PrerequisiteRevision)
                    { Say("Please wait for the service-start question before approving."); return; }
                    state = state with { ServiceStartApproval = new(EvidenceSource.User, $"Pilot service-start approval {metadata.EventId}", metadata.OccurredAt), Question = null };
                    return;
                case PilotDirective.HoldServicesStart:
                    if (state.Question?.Kind == QuestionKind.ServicesStart) Say("No problem. Let me know when you're ready to start services.");
                    return;
                case PilotDirective.Status:
                    Say(ReadyForBoarding() && state.Session.Services[ServiceKind.Boarding].State == ServiceState.Planned ? "Required services and gate checks are ready. Boarding still needs your approval." :
                        state.Session.Stage == WorkflowStage.Boarding ? "Boarding is underway. " + DelayDescription(state) :
                        state.Session.Stage == WorkflowStage.AwaitingClosureApproval ? "Boarding is complete. Waiting for your closure instruction." : DelayDescription(state));
                    return;
                case PilotDirective.Delay: Say(DelayDescription(state)); return;
                case PilotDirective.Help: Say("You can ask for status or delay, skip a planned service, cancel an active service, resume a service, or reply to the current approval question."); return;
                case PilotDirective.Unknown: Say("Please clarify the service or reply to the current question."); return;
                case PilotDirective.KeepService:
                    if (state.Question?.Kind == QuestionKind.MandatorySkip && state.Question.Service == kind)
                    { state = state with { Question = null }; Say("Keeping the planned service. No skip was applied."); }
                    return;
                case PilotDirective.SkipSimulatedDelay:
                    var simulated = state.Session.Holds.Where(hold => hold.IsActive && hold.IsSimulated).ToArray();
                    bool preparingGate = state.Realism is { Options.Enabled: true, GateProfile.SimulatedReadiness: true, GatePrepared: false };
                    if (simulated.Length == 0 && !preparingGate) { Say("There is no simulated delay to skip."); return; }
                    if (state.Realism is { } activeRealism)
                    {
                        var cleared = activeRealism with { PreparationSkipped = true, GatePrepared = activeRealism.Options.Enabled && activeRealism.GateProfile.SimulatedReadiness };
                        if (cleared.Incident is { Status: DisruptionStatus.Active } incident)
                            cleared = cleared with { Incident = incident with { Status = DisruptionStatus.Skipped, Effects = incident.Effects.Select(effect => effect with { IsActive = false }).ToImmutableArray() } };
                        ApplyRealism(cleared);
                    }
                    Invalidate(preserveServicePermissions: true);
                    state = state with { Session = state.Session with { Holds = state.Session.Holds.Select(hold => hold.IsSimulated ? hold with { IsActive = false } : hold).ToImmutableArray() } };
                    if (state.Session.Gate.State == GateReadinessState.Held)
                        state = state with { Session = state.Session with { Gate = new(GateReadinessState.Unknown, GateProvenance.Unknown, null) } };
                    Say("Simulated delays cleared. Real service and pilot holds remain in force.");
                    return;
                case PilotDirective.HoldBoarding:
                case PilotDirective.HoldClosure:
                    Invalidate();
                    if (instruction.Directive == PilotDirective.HoldClosure) state = state with { ClosureHeld = true };
                    else state = state with { Session = state.Session with { Holds = state.Session.Holds.Add(new(metadata.EventId, HoldKind.ManualPilotHold, "Boarding held by pilot", true, null, null, HoldScope.Boarding)) } };
                    Say(instruction.Directive == PilotDirective.HoldBoarding && state.Session.Services[ServiceKind.Boarding].State is ServiceState.Active or ServiceState.Requested
                        ? "Boarding is already underway. This hold blocks further clearance but does not pause GSX. Use manual controls or request supported cancellation."
                        : "Understood. I'll wait for a fresh go-ahead when you're ready.");
                    return;
                case PilotDirective.ApproveBoarding:
                    if (state.Question?.Kind != QuestionKind.Boarding || !ReadyForBoarding() || state.Question.Revision != state.Session.PrerequisiteRevision)
                    { Say("Boarding is not ready for approval yet. " + DelayDescription(state)); return; }
                    Approve(ApprovalKind.Boarding);
                    Queue(OperationalCommandKind.RequestService, ServiceKind.Boarding);
                    SetStage(WorkflowStage.Boarding);
                    return;
                case PilotDirective.ApproveClosure:
                    if (state.ClosureHeld) { state = state with { ClosureHeld = false }; Say("I'll ask for fresh closure approval."); return; }
                    if (state.Question?.Kind != QuestionKind.Closure || state.Question.Revision != state.Session.PrerequisiteRevision || Held() ||
                        !Enum.GetValues<ServiceKind>().All(Resolved) || state.Session.Services[ServiceKind.Boarding].State != ServiceState.Completed)
                    { Say("Closure needs completed boarding and a fresh pilot approval."); return; }
                    Approve(ApprovalKind.Closure);
                    SetStage(WorkflowStage.ClosingAndReleasing);
                    return;
                case PilotDirective.ConfirmGate:
                    if (state.Question?.Kind != QuestionKind.GateCheck) { Say("No gate check is awaiting confirmation."); return; }
                    Invalidate();
                    state = state with
                    {
                        Session = state.Session with
                        {
                            Gate = new(GateReadinessState.Ready, GateProvenance.PilotAttested,
                        new(EvidenceSource.User, $"Pilot gate acknowledgement {metadata.EventId}", metadata.OccurredAt))
                        }
                    };
                    return;
                case PilotDirective.ConfirmFuel:
                    if (state.Question?.Kind != QuestionKind.FuelCheck) { Say("No fuel attestation is pending."); return; }
                    state = state with { FuelAttested = true, FuelAttestation = new(EvidenceSource.User, $"Pilot fuel confirmation {metadata.EventId}", metadata.OccurredAt), Question = null };
                    return;
                case PilotDirective.ConfirmRelease:
                    if (state.Question is not { Kind: QuestionKind.ManualRelease, Release: { } action } || !HasApproval(ApprovalKind.Closure))
                    { Say("No approved manual release is awaiting confirmation."); return; }
                    if (instruction.Release is { } requestedRelease && requestedRelease != action) { Say("Please confirm the equipment currently requested."); return; }
                    state = state with
                    {
                        Releases = state.Releases.SetItem(action, new(action.ToString(), true,
                        new(EvidenceSource.User, $"Manual release acknowledgement {metadata.EventId}", metadata.OccurredAt), CompletionProvenance.PilotAttested)),
                        Question = null
                    };
                    return;
                case PilotDirective.ConfirmServiceCompletion:
                    if (state.Connection != ConnectionState.Connected || state.Question is not { Kind: QuestionKind.ServiceCompletion, Service: { } checkedService } ||
                        state.Question.Revision != state.Session.PrerequisiteRevision || !state.ServiceCompletionChecks.Contains(checkedService) ||
                        instruction.Service is { } selected && selected != checkedService)
                    { Say("No matching service completion check is awaiting confirmation."); return; }
                    Invalidate();
                    state = state with { ServiceCompletionChecks = state.ServiceCompletionChecks.Remove(checkedService) };
                    SetService(checkedService, ServiceState.Completed, new(EvidenceSource.User, $"Pilot checked service completion {metadata.EventId}", metadata.OccurredAt));
                    Say($"{checkedService} completion recorded from your manual confirmation.");
                    return;
                case PilotDirective.ApproveService:
                    if (kind is null || state.Question?.Kind != QuestionKind.Service || state.Question.Service != kind)
                    { Say("No matching service permission is pending."); return; }
                    state = state with
                    {
                        ServicePermissions = state.ServicePermissions.Add(kind.Value),
                        ServicePermissionEvidence = state.ServicePermissionEvidence.SetItem(kind.Value, new(EvidenceSource.User, $"Pilot service approval {metadata.EventId}", metadata.OccurredAt)),
                        Question = null
                    };
                    return;
            }
            if (kind is null || !state.Session.Services.TryGetValue(kind.Value, out var service)) { Say("Which service do you mean?"); return; }
            if (instruction.Directive == PilotDirective.Resume && kind == ServiceKind.Boarding && state.Session.Holds.Any(hold => hold.IsActive && hold.Kind == HoldKind.ManualPilotHold && hold.Scope == HoldScope.Boarding))
            {
                Invalidate();
                state = state with { Session = state.Session with { Holds = state.Session.Holds.Select(hold => hold.Kind == HoldKind.ManualPilotHold && hold.Scope == HoldScope.Boarding ? hold with { IsActive = false } : hold).ToImmutableArray() } };
                return;
            }
            if (instruction.Directive == PilotDirective.Resume && service.State == ServiceState.Unknown &&
                (state.Connection != ConnectionState.Connected || state.TriggerableServices?.Contains(kind.Value) != true))
            { Say("Review and reconcile the service first. A restart needs a fresh available state from GSX; it does not confirm the previous work completed."); return; }
            if (instruction.Directive == PilotDirective.Resume && service.State is ServiceState.Skipped or ServiceState.Cancelled or ServiceState.Failed or ServiceState.NotRequired or ServiceState.CycleEnded or ServiceState.Unknown && ServicePlanRules.Applicable(kind.Value, state.Session.Operation, state.Session.Plan.Services[kind.Value].Rule))
            {
                Invalidate(); SetService(kind.Value, ServiceState.Planned, null);
                state = state with
                {
                    PilotOverrides = state.PilotOverrides.Add(kind.Value),
                    ServiceCompletionChecks = state.ServiceCompletionChecks.Remove(kind.Value),
                    ServicePermissions = state.ServicePermissions.Add(kind.Value),
                    ServicePermissionEvidence = state.ServicePermissionEvidence.SetItem(kind.Value, new(EvidenceSource.User, $"Pilot resume {metadata.EventId}", metadata.OccurredAt))
                };
                if (kind == ServiceKind.Refuelling) state = state with { FuelAttested = false, FuelAttestation = null };
                Say($"{kind} replanned at your request. Previous work is not marked complete; I'll wait for its current prerequisites and GSX availability.");
                return;
            }
            if (instruction.Directive == PilotDirective.Cancel)
            {
                if (service.State is not (ServiceState.Requested or ServiceState.Active)) { Say("That service is not active. Skip it before it starts, or wait for its current request to be reconciled."); return; }
                if (!state.CancellableServices.Contains(kind.Value)) { Say("Cancellation is unsupported. Please use GSX manually; I cannot confirm it stopped."); return; }
                Invalidate(); state = state with { PilotOverrides = state.PilotOverrides.Add(kind.Value) }; Queue(OperationalCommandKind.CancelService, kind);
                if (kind == ServiceKind.Refuelling) state = state with { FuelAttested = false, FuelAttestation = null };
                Say("Cancellation requested. I'll wait for confirmation that it stopped."); return;
            }
            if (instruction.Directive is PilotDirective.Skip or PilotDirective.ConfirmSkip)
            {
                if (service.State is not (ServiceState.Planned or ServiceState.Eligible)) { Say("That service has already been requested or resolved. An active service needs cancellation, not skipping."); return; }
                if (state.Session.Plan.Services[kind.Value].Rule.Required && (instruction.Directive != PilotDirective.ConfirmSkip ||
                    state.Question?.Kind != QuestionKind.MandatorySkip || state.Question.Service != kind))
                { Ask(QuestionKind.MandatorySkip, "skip.confirm", $"{kind} is required. Confirm skipping it? This does not prove the work is complete or bypass readiness checks.", kind); return; }
                Invalidate(); state = state with { PilotOverrides = state.PilotOverrides.Add(kind.Value) }; SetService(kind.Value, ServiceState.Skipped, new(EvidenceSource.User, $"Pilot skip {metadata.EventId}", metadata.OccurredAt));
                if (kind == ServiceKind.Refuelling) state = state with { FuelAttested = false, FuelAttestation = null };
                Say($"{kind} skipped for this session."); return;
            }
            Say("That action is not available for the service's current state.");
        }
        void Approve(ApprovalKind kind)
        {
            state = state with
            {
                Question = null,
                Session = state.Session with
                {
                    Approvals = state.Session.Approvals.Add(new(metadata.EventId,
                state.Session.Id, kind, state.Session.PrerequisiteRevision, metadata.OccurredAt, false))
                }
            };
        }
        void Schedule()
        {
            if (state.Connection == ConnectionState.Connected && state.ServiceCompletionChecks.Count > 0)
            {
                var pending = state.ServiceCompletionChecks.Order().First();
                SetStage(WorkflowStage.NeedsAttention);
                Ask(QuestionKind.ServiceCompletion, "service.completion.check",
                    $"GSX returned {pending.ToString().ToLowerInvariant()} to available without confirming completion. Please check the work is finished, then confirm service complete. I'll wait before continuing.", pending);
                return;
            }
            if (state.Connection != ConnectionState.Connected || state.Session.Services.Values.Any(service => service.State is ServiceState.Unknown or ServiceState.Failed))
            { SetStage(WorkflowStage.NeedsAttention); return; }
            if (state.RequiresServiceStartApproval && state.ServiceStartApproval is null)
            {
                SetStage(WorkflowStage.Planning);
                Ask(QuestionKind.ServicesStart, state.GsxFlightPlanReceived ? "services.start.gsx" : "services.start.approval",
                    state.GsxFlightPlanReceived ? state.PlannedLoadFiguresReceived ? "Flight plan and planned load figures received from GSX. Happy for me to start services?" : "Flight plan received from GSX. Happy for me to start services?" : "Are we ready to start services?");
                return;
            }
            if (state.Session.Stage == WorkflowStage.ClosingAndReleasing && !HasApproval(ApprovalKind.Closure))
            { SetStage(WorkflowStage.AwaitingClosureApproval); }
            if (HasApproval(ApprovalKind.Closure) && state.Session.Services[ServiceKind.Boarding].State == ServiceState.Completed && Enum.GetValues<ServiceKind>().All(Resolved) && !Held())
            {
                SetStage(WorkflowStage.ClosingAndReleasing);
                foreach (var action in Enum.GetValues<ReleaseAction>())
                {
                    if (state.Releases[action].IsSatisfied) continue;
                    if (state.SupportedReleaseActions.Contains(action)) Queue(OperationalCommandKind.ReleaseEquipment, release: action);
                    else Ask(QuestionKind.ManualRelease, "release.manual", action switch
                    {
                        ReleaseAction.Doors => "Please close the required passenger and cargo doors, then confirm they are closed.",
                        ReleaseAction.JetwayOrStairs => "Please disconnect the jetway or remove the passenger stairs, then confirm they are clear.",
                        _ => "Please disconnect the remaining ground equipment and remove chocks where appropriate, then confirm it is clear."
                    }, release: action);
                    return;
                }
                SetStage(WorkflowStage.Completed);
                state = state with { Question = null };
                string provenance = state.Releases.Values.Any(release => release.Provenance == CompletionProvenance.Simulation) ? "simulated" :
                    state.Releases.Values.Any(release => release.Provenance == CompletionProvenance.PilotAttested) ? "attested" : "verified";
                notices.Add(new($"handover.{provenance}", Important: true));
                return;
            }
            if (state.Session.Services[ServiceKind.Boarding].State == ServiceState.Completed)
            {
                SetStage(WorkflowStage.AwaitingClosureApproval);
                if (!Held() && !state.ClosureHeld && Enum.GetValues<ServiceKind>().All(Resolved)) Ask(QuestionKind.Closure, "closure.approval");
                else if (input is PilotInstruction { Directive: PilotDirective.ApproveClosure }) state = state with { ClosureHeld = false };
                return;
            }
            if (state.Session.Services[ServiceKind.Boarding].State is ServiceState.RequestPending or ServiceState.Requested or ServiceState.Active or ServiceState.CancelPending)
            { SetStage(WorkflowStage.Boarding); return; }
            if (state.Session.Holds.Any(hold => hold.IsActive && !hold.IsSimulated && hold.Scope == HoldScope.AllOperations))
            { SetStage(WorkflowStage.PreBoardServices); return; }
            foreach (var kind in ServiceSequence.Ordered(state.Session.Plan).Where(kind => kind != ServiceKind.Boarding))
            {
                var service = state.Session.Services[kind];
                if (service.State is not (ServiceState.Planned or ServiceState.Eligible) || !ServicePlanRules.Dependencies(state.Session.Plan, kind).All(Resolved)) continue;
                if (state.TriggerableServices is { } available && !available.Contains(kind)) continue;
                if (!ServiceSequence.CanOverlap(state, kind)) continue;
                SetStage(kind is ServiceKind.Deboarding or ServiceKind.Cleaning ? WorkflowStage.ArrivalHandling : WorkflowStage.PreBoardServices);
                if (!state.SupportedServices.Contains(kind))
                { SetStage(WorkflowStage.NeedsAttention); Ask(QuestionKind.Service, "service.unsupported", $"{kind} needs manual handling; automatic control is unsupported.", kind); return; }
                if (state.Question is { Kind: QuestionKind.MandatorySkip }) return;
                if (state.Session.Plan.Services[kind].Rule.Policy == ServicePolicy.Ask && (!state.ServicePermissions.Contains(kind) || !state.ServicePermissionEvidence.ContainsKey(kind)))
                { Ask(QuestionKind.Service, "service.approval", $"Would you like me to request {kind.ToString().ToLowerInvariant()}?", kind); return; }
                Queue(OperationalCommandKind.RequestService, kind);
            }
            if (state.Session.Operation == FlightOperationType.FinalArrival && Enum.GetValues<ServiceKind>().All(Resolved))
            { SetStage(WorkflowStage.Completed); notices.Add(new("arrival.complete", Important: true)); return; }
            if (state.Session.Services[ServiceKind.Refuelling].State == ServiceState.Skipped && !state.FuelAttested && state.Session.Plan.Services[ServiceKind.Refuelling].Rule.Required)
            { Ask(QuestionKind.FuelCheck, "fuel.check", "Refuelling was skipped, not completed. Please confirm independently that the required fuel is aboard."); return; }
            if (state.Session.Services[ServiceKind.Boarding].State is ServiceState.Skipped or ServiceState.Cancelled or ServiceState.NotRequired)
            { SetStage(WorkflowStage.NeedsAttention); return; }
            if (!Enum.GetValues<ServiceKind>().Where(kind => kind != ServiceKind.Boarding).All(Resolved)) return;
            if (Held()) { SetStage(WorkflowStage.PreBoardServices); state = state with { Question = null }; return; }
            if (state.Session.Gate.State != GateReadinessState.Ready)
            { SetStage(WorkflowStage.PreBoardServices); if (state.Session.Gate.State is GateReadinessState.Unknown or GateReadinessState.Invalidated) Ask(QuestionKind.GateCheck, "gate.check", "Gate readiness is unknown. Please check the gate and confirm before boarding."); return; }
            if (!state.SupportedServices.Contains(ServiceKind.Boarding)) { SetStage(WorkflowStage.NeedsAttention); return; }
            if (state.TriggerableServices is { } triggerable && !triggerable.Contains(ServiceKind.Boarding)) { SetStage(WorkflowStage.PreBoardServices); return; }
            SetStage(WorkflowStage.AwaitingBoardingApproval);
            Ask(QuestionKind.Boarding, state.Session.Services[ServiceKind.Refuelling].State == ServiceState.Completed ? "boarding.approval" : "boarding.approval.no-fuel");
        }
    }

    public static bool EvidenceValid(Evidence? evidence) => evidence is { Reference.Length: > 0 } && evidence.Source is EvidenceSource.Gsx or EvidenceSource.SimConnect or EvidenceSource.User or EvidenceSource.Simulation;
    private static CompletionProvenance Provenance(Evidence evidence) => evidence.Source switch
    { EvidenceSource.User => CompletionProvenance.PilotAttested, EvidenceSource.Simulation => CompletionProvenance.Simulation, _ => CompletionProvenance.VerifiedExternal };
    public static string DelayDescription(OperationalState state)
    {
        var activeHolds = state.Session.Holds.Where(hold => hold.IsActive).ToArray();
        var holds = activeHolds.Where(hold => !activeHolds.Any(real => !real.IsSimulated) || !hold.IsSimulated).Select(hold => hold.Reason).Distinct().ToArray();
        if (holds.Length > 0) return string.Join("; ", holds);
        if (state.Connection != ConnectionState.Connected) return "Connection reconciliation is required; nothing will be replayed automatically.";
        if (state.Session.Services[ServiceKind.Refuelling].State == ServiceState.Skipped && !state.FuelAttested && state.Session.Plan.Services[ServiceKind.Refuelling].Rule.Required)
            return "Fuel was skipped; confirmation that the required fuel is aboard is still needed.";
        var unresolved = state.Session.Services.Values.Where(service => service.Kind != ServiceKind.Boarding && service.State is not (ServiceState.Completed or ServiceState.NotRequired or ServiceState.Skipped or ServiceState.CycleEnded)).OrderBy(service => service.Kind)
            .Select(service => $"{service.Kind} " + (service.State switch
            {
                ServiceState.Active => "is in progress",
                ServiceState.Requested => "has been requested",
                ServiceState.RequestPending => "is awaiting request confirmation",
                ServiceState.CancelPending => "cancellation is awaiting confirmation",
                ServiceState.Cancelled => "was cancelled",
                ServiceState.Failed => "needs attention",
                ServiceState.Unknown => "status needs review",
                _ => "is waiting to start"
            })).ToArray();
        return unresolved.Length > 0 ? string.Join("; ", unresolved) : "Waiting for gate checks or the current pilot approval.";
    }
}
