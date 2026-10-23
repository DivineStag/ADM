using System.Collections.Immutable;
using ADM.Domain;

namespace ADM.Contracts;

public interface IOperationalClock { DateTimeOffset UtcNow { get; } }
public interface IRandomSource { double NextDouble(); int Next(int min, int max); }

public sealed record SimulatorSnapshot(DateTimeOffset ObservedAt, ConnectionState Connection,
    bool? OnGround, double? GroundSpeedKnots, bool? ParkingBrakeSet, bool? EnginesRunning,
    string? AircraftTitle, string? AirportIcao, string? Stand, string? AtcCallsign,
    EvidenceSource Source)
{
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}
public interface ISimulatorTelemetry
{
    IAsyncEnumerable<SimulatorSnapshot> WatchAsync(CancellationToken cancellationToken);
}

public enum ReceiptStatus { Accepted, Rejected, Unknown }
// Deliberately no Pushback value or arbitrary wire-command escape hatch.
public enum GsxAction { CloseSupportedDoors, ReleaseJetwayOrStairs, ReleaseSupportedGroundEquipment }
public sealed record GsxCapabilities(string Adapter, string? VerifiedApiVersion, bool IsFake,
    ImmutableHashSet<ServiceKind> RequestableServices, ImmutableHashSet<ServiceKind> CancellableServices,
    ImmutableHashSet<GsxAction> SupportedActions);
public sealed record ServiceCommand(Guid CommandId, Guid SessionId, ServiceKind Kind, int IntentVersion,
    Guid PlanSnapshotId, string PolicySource);
public sealed record ActionCommand(Guid CommandId, Guid SessionId, GsxAction Action);
public sealed record CommandReceipt(Guid CommandId, ReceiptStatus Status, string Detail);
public sealed record GsxEvent(EventMetadata Metadata, ServiceKind Kind, ServiceState State);
public interface IGsxGateway
{
    Task<GsxCapabilities> ProbeAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<GsxEvent> WatchAsync(CancellationToken cancellationToken);
    Task<CommandReceipt> RequestServiceAsync(ServiceCommand command, CancellationToken cancellationToken);
    Task<CommandReceipt> CancelServiceAsync(ServiceCommand command, CancellationToken cancellationToken);
    Task<CommandReceipt> ExecuteVerifiedActionAsync(ActionCommand command, CancellationToken cancellationToken);
}

public sealed record GsxServiceStatus(ServiceState State, bool CanTrigger, bool Waiting, Evidence Evidence)
{
    public bool IsIdleUnavailable { get; init; }
}
public sealed record GsxLiveSnapshot(ConnectionState Connection, long Generation, DateTimeOffset ReceivedAt,
    ImmutableDictionary<ServiceKind, GsxServiceStatus> Services, string? AirportIcao, string? Stand, string? AircraftTitle)
{
    public FlightPlanSnapshot? FlightPlan { get; init; }
    public string? AircraftIcaoType { get; init; }
    public string? AircraftSetupKey { get; init; }
    public bool AwaitingInput { get; init; }
    public bool MenuOpen { get; init; }
}
public interface IGsxLiveState
{
    GsxLiveSnapshot Snapshot { get; }
}

/// <summary>Explicit pilot restart only: refresh provider state before releasing an uncertain-service block.</summary>
public interface IGsxRestartRecovery
{
    Task<bool> PrepareRestartAsync(ServiceKind service, long expectedGeneration, CancellationToken cancellationToken);
}

public sealed record FlightPlanResult(FlightPlanSnapshot? Plan, string? Error);
public interface IFlightPlanImporter
{
    Task<FlightPlanResult> ImportLatestAsync(string pilotId, CancellationToken cancellationToken);
}
public interface IAirlineIdentityResolver
{
    AirlineResolution Resolve(FlightIdentity identity, UserAirlineOverride? manualOverride);
}
public interface IUserAirlineRegistry
{
    void RegisterProfileDesignator(string icao);
}
public interface IAirlineProfileRepository
{
    Task<IReadOnlyList<AirlineProfile>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AirlineProfile>>([]);
    Task<AirlineProfile?> FindAsync(string icao, CancellationToken cancellationToken);
    Task SaveAsync(AirlineProfile profile, CancellationToken cancellationToken);
}
public interface IServicePlanResolver
{
    AppliedServicePlan Resolve(AirlineProfile genericProfile, AirlineProfile? airlineProfile,
        AircraftIdentity aircraft, FlightOperationType operation, PilotServiceOverrides overrides);
}
public interface ISessionRepository
{
    Task<DutySession?> LoadAsync(Guid dutyId, CancellationToken cancellationToken);
    Task SaveAsync(DutySession duty, CancellationToken cancellationToken);
}
