using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application;

public sealed record DispatchCheckpoint(int SchemaVersion, OperationalState State,
    ImmutableArray<ConversationMessage> Messages, ImmutableHashSet<string> DeliveredKeys)
{
    public int VisibleMessageOffset { get; init; }
}

/// <summary>Serialized reducer and conversation use case. The live coordinator owns all external effects.</summary>
public sealed class LocalDispatchSession
{
    private readonly object _sync = new();
    private readonly IConversationEngine _conversation;
    private readonly IOperationalClock _clock;
    private DispatchCheckpoint _checkpoint;
    public LocalDispatchSession(OperationalState state, IConversationEngine conversation, IOperationalClock clock)
        : this(new DispatchCheckpoint(1, state, [], []), conversation, clock, false)
    {
        Publish(new("welcome", Important: true), Metadata(EvidenceSource.System));
    }
    public LocalDispatchSession(DispatchCheckpoint checkpoint, IConversationEngine conversation, IOperationalClock clock, bool requiresReconciliation = true)
    {
        DispatchCheckpointCodec.Validate(checkpoint);
        _checkpoint = checkpoint;
        _conversation = conversation;
        _clock = clock;
        if (requiresReconciliation) Accept(new ChangeConnection(Metadata(EvidenceSource.System), ConnectionState.ReconciliationRequired));
    }
    public void ClearChat() { lock (_sync) _checkpoint = _checkpoint with { VisibleMessageOffset = _checkpoint.Messages.Length }; }
    public DispatchCheckpoint Checkpoint { get { lock (_sync) return _checkpoint; } }
    public OperationalContext Context
    {
        get
        {
            lock (_sync) return new(_checkpoint.State.Session, _checkpoint.State.Connection,
                _checkpoint.State.Question, _checkpoint.State.FocusService, _checkpoint.Messages.Where(message => !message.IsSystem && message.Sender != "You").TakeLast(8).Select(message => message.Text).ToImmutableArray());
        }
    }
    public Reduction Accept(OperationEvent input)
    {
        lock (_sync)
        {
            var previous = _checkpoint.State;
            var reduced = OperationalReducer.Apply(previous, input);
            _checkpoint = _checkpoint with { State = reduced.State };
            PublishServiceUpdate(previous, input);
            var delivered = ImmutableArray.CreateBuilder<WorkflowNotice>();
            foreach (var notice in reduced.Notices) if (Publish(notice, input.Metadata)) delivered.Add(notice);
            return reduced with { Notices = delivered.ToImmutable() };
        }
    }
    private void PublishServiceUpdate(OperationalState previous, OperationEvent input)
    {
        ServiceKind? kind = input switch { ObserveService observation => observation.Kind, ObserveServiceCycleEnd ended => ended.Kind, ReconcileService reconciled when previous.Connection == ConnectionState.Connected && previous.Session.Services[reconciled.Kind].State is ServiceState.Requested or ServiceState.Active => reconciled.Kind, _ => null };
        if (kind is null || kind == ServiceKind.Deboarding) return;
        var service = _checkpoint.State.Session.Services[kind.Value];
        if (previous.Session.Services[kind.Value].State == service.State) return;
        string? suffix = service.State switch
        {
            ServiceState.Active => "started",
            ServiceState.Completed => "completed",
            ServiceState.CycleEnded => "returned idle",
            ServiceState.Cancelled => "cancelled",
            _ => null
        };
        if (suffix is null) return;
        var intent = _checkpoint.State.Commands.LastOrDefault(command => command.Kind == OperationalCommandKind.RequestService && command.Service == kind);
        string key = $"system.service:{kind}:{intent?.Id.ToString() ?? "initial"}:{suffix}";
        if (_checkpoint.DeliveredKeys.Contains(key)) return;
        string name = kind == ServiceKind.Refuelling ? "Refuelling" : kind.ToString()!;
        _checkpoint = _checkpoint with
        {
            DeliveredKeys = _checkpoint.DeliveredKeys.Add(key),
            Messages = _checkpoint.Messages.Add(new(input.Metadata.EventId, input.Metadata.SessionId, key, "",
                name + " " + suffix, input.Metadata.OccurredAt)
            { IsSystem = true })
        };
    }
    public Reduction Send(string text)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(text)) return new(_checkpoint.State, [], []);
            var metadata = Metadata(EvidenceSource.User);
            _checkpoint = _checkpoint with
            {
                Messages = _checkpoint.Messages.Add(new(metadata.EventId, metadata.SessionId,
                $"pilot:{metadata.EventId}", "You", text.Trim(), metadata.OccurredAt))
            };
            var intent = _conversation.Parse(text, Context);
            string? chatKey = intent.Kind switch
            {
                PilotIntentKind.Greeting => "greeting",
                PilotIntentKind.Wellbeing => "wellbeing",
                PilotIntentKind.Busy => "busy",
                PilotIntentKind.Farewell => "farewell",
                PilotIntentKind.AllGood => "all-good",
                PilotIntentKind.Acknowledgement => text.Contains("no", StringComparison.OrdinalIgnoreCase) && !text.Contains("thank", StringComparison.OrdinalIgnoreCase) ? "wait" : "thanks",
                PilotIntentKind.Unknown => "unknown",
                _ => null
            };
            if (chatKey is not null)
            {
                Publish(new($"chat.{chatKey}:{metadata.EventId}"), metadata);
                return new(_checkpoint.State, [], []);
            }
            PilotDirective directive = intent.Kind switch
            {
                PilotIntentKind.GetStatus => PilotDirective.Status,
                PilotIntentKind.GetDelay => PilotDirective.Delay,
                PilotIntentKind.ApproveBoarding => PilotDirective.ApproveBoarding,
                PilotIntentKind.HoldBoarding => PilotDirective.HoldBoarding,
                PilotIntentKind.ApproveClosure => PilotDirective.ApproveClosure,
                PilotIntentKind.HoldClosure => PilotDirective.HoldClosure,
                PilotIntentKind.SkipService => PilotDirective.Skip,
                PilotIntentKind.SkipToService => PilotDirective.SkipTo,
                PilotIntentKind.CancelService => PilotDirective.Cancel,
                PilotIntentKind.ResumeService => PilotDirective.Resume,
                PilotIntentKind.SkipSimulatedDelay => PilotDirective.SkipSimulatedDelay,
                PilotIntentKind.ApproveService => PilotDirective.ApproveService,
                PilotIntentKind.ConfirmSkip => PilotDirective.ConfirmSkip,
                PilotIntentKind.ConfirmGate => PilotDirective.ConfirmGate,
                PilotIntentKind.ConfirmFuel => PilotDirective.ConfirmFuel,
                PilotIntentKind.ConfirmRelease => PilotDirective.ConfirmRelease,
                PilotIntentKind.ConfirmServiceCompletion => PilotDirective.ConfirmServiceCompletion,
                PilotIntentKind.ApproveServicesStart => PilotDirective.ApproveServicesStart,
                PilotIntentKind.HoldServicesStart => PilotDirective.HoldServicesStart,
                PilotIntentKind.KeepService => PilotDirective.KeepService,
                PilotIntentKind.GetHelp => PilotDirective.Help,
                _ => PilotDirective.Unknown
            };
            return Accept(new PilotInstruction(metadata, directive, intent.Service, intent.Release));
        }
    }
    public EventMetadata Metadata(EvidenceSource source, string? evidence = null)
    {
        Guid id = Guid.NewGuid();
        return new(id, _checkpoint.State.Session.Id, _clock.UtcNow, source, id,
            evidence is null ? null : new Evidence(source, evidence, _clock.UtcNow));
    }
    private bool Publish(WorkflowNotice notice, EventMetadata metadata)
    {
        if (_checkpoint.DeliveredKeys.Contains(notice.Key)) return false;
        // Flight/gate figure refreshes may revise a still-unanswered start question.
        // Keep its new domain revision, but do not repeat the same invitation in chat.
        if (notice.Key.StartsWith("services.start.", StringComparison.Ordinal) &&
            _checkpoint.Messages.Skip(_checkpoint.VisibleMessageOffset).Any(message => message.SemanticKey.StartsWith("services.start.", StringComparison.Ordinal)) &&
            _checkpoint.State.ServiceStartApproval is null)
        {
            _checkpoint = _checkpoint with { DeliveredKeys = _checkpoint.DeliveredKeys.Add(notice.Key) };
            return false;
        }
        var decision = _conversation.Evaluate(Context, new(metadata, notice.Key, notice.Text));
        if (string.IsNullOrWhiteSpace(decision.Message)) return false;
        _checkpoint = _checkpoint with
        {
            DeliveredKeys = _checkpoint.DeliveredKeys.Add(notice.Key),
            Messages = _checkpoint.Messages.Add(new(new Guid(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{metadata.SessionId}/{metadata.EventId}/{notice.Key}")).AsSpan(0, 16)), metadata.SessionId, notice.Key,
                _checkpoint.State.Session.Dispatcher.Persona.Name, decision.Message, metadata.OccurredAt))
        };
        return true;
    }
}

public static class DispatchCheckpointCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }, WriteIndented = true };
    public static string Serialize(DispatchCheckpoint checkpoint) => JsonSerializer.Serialize(checkpoint, Options);
    public static DispatchCheckpoint Deserialize(string json)
    {
        var checkpoint = JsonSerializer.Deserialize<DispatchCheckpoint>(json, Options);
        Validate(checkpoint);
        return checkpoint!;
    }
    public static void Validate(DispatchCheckpoint? checkpoint)
    {
        if (checkpoint is null || checkpoint.SchemaVersion != 1 || checkpoint.State?.Session is not { } session ||
            session.Id == Guid.Empty || session.Dispatcher?.SessionId != session.Id || session.Plan is null || session.Services is null ||
            session.Approvals.IsDefault || session.Holds.IsDefault || session.Gate is null || checkpoint.Messages.IsDefault ||
            checkpoint.DeliveredKeys is null || checkpoint.State.Commands.IsDefault || checkpoint.State.SeenEvents is null ||
            checkpoint.State.AcknowledgedCommands is null || checkpoint.State.ServicePermissions is null || checkpoint.State.Releases is null ||
            checkpoint.State.SupportedServices is null || checkpoint.State.CancellableServices is null || checkpoint.State.SupportedReleaseActions is null ||
            checkpoint.State.ServicePermissionEvidence is null || checkpoint.State.PilotOverrides is null || checkpoint.State.ServiceCompletionChecks is null ||
            checkpoint.State.ServiceCompletionChecks.Any(kind => !Enum.IsDefined(kind)) || checkpoint.State.ValidatedConcurrentGroups.IsDefault ||
            checkpoint.State.ValidatedConcurrentGroups.Any(group => group is null || group.Contains(ServiceKind.Boarding) || group.Any(kind => !Enum.IsDefined(kind))) ||
            !Enum.IsDefined(session.Stage) || !Enum.IsDefined(checkpoint.State.Connection) ||
            checkpoint.State.SkipToTarget is { } target && (!Enum.IsDefined(target) || target == ServiceKind.Deboarding))
            throw new ArgumentException("Checkpoint schema or required fields are invalid.");
        if (session.Services.Count != 7 || session.Services.Any(pair => pair.Value is null || pair.Key != pair.Value.Kind ||
            pair.Value.SessionId != session.Id || !Enum.IsDefined(pair.Value.State) || pair.Value.State == ServiceState.Completed && !OperationalReducer.EvidenceValid(pair.Value.LastEvidence) ||
            pair.Value.State == ServiceState.CycleEnded && (pair.Key is ServiceKind.Refuelling or ServiceKind.Boarding || session.Plan.Services[pair.Key].Rule.Required || pair.Value.LastEvidence?.Source != EvidenceSource.Gsx)) ||
            session.Approvals.Any(approval => approval is null || approval.SessionId != session.Id || approval.PrerequisiteRevision > session.PrerequisiteRevision) ||
            checkpoint.Messages.Any(message => message is null || message.SessionId != session.Id || message.Id == Guid.Empty) ||
            checkpoint.Messages.Select(message => message.Id).Distinct().Count() != checkpoint.Messages.Length ||
            checkpoint.State.Commands.Any(command => command is null || command.SessionId != session.Id || command.Id == Guid.Empty || !Enum.IsDefined(command.Kind)))
            throw new ArgumentException("Checkpoint identities or evidence are invalid.");
        if (checkpoint.VisibleMessageOffset < 0 || checkpoint.VisibleMessageOffset > checkpoint.Messages.Length) throw new ArgumentException("Invalid chat visibility boundary.");
        GateRules.Apply(session.Gate);
        checkpoint.State.Realism?.Validate();
        if (checkpoint.State.Realism?.Incident is { } incident && incident.SessionId != session.Id) throw new ArgumentException("Disruption belongs to a different session.");
        if (checkpoint.State.ServiceStartApproval is { } startApproval && (startApproval.Source != EvidenceSource.User || !OperationalReducer.EvidenceValid(startApproval)))
            throw new ArgumentException("Service-start approval requires explicit pilot evidence.");
        ServicePlanRules.Validate(session.Plan);
    }
}
