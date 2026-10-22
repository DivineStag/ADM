using System.Collections.Immutable;
using ADM.Domain;

namespace ADM.Contracts;

public sealed record OperationalContext(GroundHandlingSession Session, ConnectionState Connection,
    PendingQuestion? Question = null, ServiceKind? FocusService = null, ImmutableArray<string> RecentMessages = default);
public enum PilotIntentKind
{
    GetStatus, GetDelay, ApproveBoarding, HoldBoarding, ApproveClosure,
    HoldClosure, SkipService, CancelService, ResumeService, SkipSimulatedDelay, GetHelp, Acknowledgement, Unknown,
    ApproveService, ConfirmSkip, ConfirmGate, ConfirmFuel, ConfirmRelease, KeepService, ConfirmServiceCompletion, ApproveServicesStart, HoldServicesStart
}
public sealed record PilotIntent(PilotIntentKind Kind, ServiceKind? Service, ReleaseAction? Release = null);
public sealed record ConversationDecision(string? SemanticKey, string? Message, ImmutableArray<string> QuickReplies);
public interface IOperationalRealismEngine
{
    OperationalRealismState Advance(OperationalState state, DateTimeOffset now);
}
public interface IAirportGateProfiles
{
    AirportGateProfile Resolve(string airportIcao);
}
public interface IProfileTransfer
{
    Task<AirlineProfile> ReadAsync(string path, CancellationToken cancellationToken);
    Task WriteAsync(string path, AirlineProfile profile, CancellationToken cancellationToken);
}
public interface IConversationEngine
{
    ConversationDecision Evaluate(OperationalContext context, DomainEvent domainEvent);
    PilotIntent Parse(string message, OperationalContext context);
}
public sealed record GateTransition(GateReadiness Readiness, bool InvalidateApproval, string Reason);
public interface IGateReadinessCoordinator
{
    GateReadiness Evaluate(OperationalContext context);
    GateTransition Apply(GateReadiness next, GateReadiness current);
}
public sealed record DutyTransition(DutySession Duty, bool RequiresPilotConfirmation);
public interface IDutyCoordinator
{
    DutyTransition Apply(DomainEvent domainEvent, DutySession current);
}
public sealed record RecoveryOutcome(bool CanResumeCommands, RecoveryCheckpoint Checkpoint, string Detail);
public interface IRecoveryCoordinator
{
    Task<RecoveryOutcome> ReconcileAsync(RecoveryCheckpoint checkpoint, CancellationToken cancellationToken);
}
public sealed record UserSetupAttestation(bool Any2GsxIntegrationEnabled, bool CompetingAutomationDisabled,
    DateTimeOffset AttestedAt);
public interface ICompatibilityAdvisor
{
    CompatibilityReport Assess(GsxCapabilities capabilities, UserSetupAttestation setup);
}
