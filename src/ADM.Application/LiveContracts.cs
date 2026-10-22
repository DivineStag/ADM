using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application;

public sealed record SavedDispatch(DispatchCheckpoint Checkpoint, FlightPlanSnapshot Flight, string AircraftBinding, bool Archived = false)
{
    public DutyProgress? Duty { get; init; }
}
public interface IDutyStore
{
    Task<DutyProgress?> LoadDutyAsync(CancellationToken ct);
    Task SaveDutyAsync(DutyProgress duty, CancellationToken ct);
    Task<ImmutableArray<SavedDispatch>> HistoryAsync(Guid dutyId, CancellationToken ct);
}
public interface IDispatchStore
{
    Task<SavedDispatch?> LoadActiveAsync(CancellationToken cancellationToken);
    Task SaveAsync(SavedDispatch operation, CancellationToken cancellationToken);
    Task<bool> TryBeginSendAsync(SavedDispatch operation, OperationalCommand command, CancellationToken cancellationToken);
    Task RecordReceiptAsync(SavedDispatch operation, Guid commandId, ReceiptStatus status, CancellationToken cancellationToken);
    Task<ImmutableHashSet<Guid>> AttemptedAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<ImmutableHashSet<Guid>> RejectedAsync(Guid sessionId, CancellationToken cancellationToken);
}
public interface IFlightPlanCache
{
    Task SaveAsync(FlightPlanSnapshot flight, CancellationToken cancellationToken);
    Task<FlightPlanSnapshot?> LoadAsync(CancellationToken cancellationToken);
}
public sealed record AircraftServiceCapabilities(string IcaoType, ImmutableHashSet<ServiceKind> Services,
    ImmutableArray<ImmutableHashSet<ServiceKind>> ConcurrentGroups, string EvidenceReference);
public sealed record AutomationSetup(bool CompetingAutomationDisabled, bool AircraftIntegrationReviewed,
    bool FlightAircraftConfirmed, string AirportIcao, string AircraftType, FlightOperationType Operation,
    UserAirlineOverride? AirlineOverride = null, bool ExistingServicesBelongToFlight = false)
{
    public bool SectorTransitionConfirmed { get; init; }
    public RealismOptions Realism { get; init; } = RealismOptions.Disabled;
    public AirportGateProfile GateProfile { get; init; } = AirportGateProfile.Generic;
}

public static class FlightPlanValidation
{
    public static void Validate(FlightPlanSnapshot flight, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(flight.Callsign) || flight.Callsign.Length > 16 || flight.Callsign.Any(character => !char.IsAsciiLetterOrDigit(character)) ||
            !Airport(flight.Origin) || !Airport(flight.Destination) || flight.Origin == flight.Destination ||
            CanonicalAircraftIdentity.Create(flight.Aircraft.IcaoType, flight.Aircraft.Family).IcaoType is null)
            throw new ArgumentException("Flight plan needs valid callsign, airports and ICAO aircraft type.");
        if (now - flight.CreatedAt > TimeSpan.FromHours(24) || flight.CreatedAt - now > TimeSpan.FromMinutes(30))
            throw new ArgumentException("Flight plan is stale or dated in the future. Generate a current OFP or enter a current flight.");
    }
    public static bool Airport(string? value) => value is { Length: 4 } && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');
}
