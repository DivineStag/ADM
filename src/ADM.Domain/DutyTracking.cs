using System.Collections.Immutable;

namespace ADM.Domain;

public sealed record DutySector(Guid Id, Guid SessionId, int Number, FlightPlanSnapshot Flight,
    DispatchAssignment Dispatcher, Guid PlanId, string Airline, int ProfileRevision, bool HandlingCompleted, bool Archived);
public sealed record ArrivalCandidate(string? Airport, string? Stand, string Reason, DateTimeOffset ObservedAt);
public sealed record DutyObservation(DateTimeOffset At, bool OnGround, double Speed, bool Parked, string Aircraft,
    string? Airport, string? Stand, double? Latitude = null, double? Longitude = null);
public sealed record DutyProgress(Guid Id, DutyState State, ImmutableArray<DutySector> Sectors)
{
    public string? Aircraft { get; init; }
    public DateTimeOffset? LastObservedAt { get; init; }
    public DateTimeOffset? AirborneSince { get; init; }
    public bool DepartureObserved { get; init; }
    public bool RequiresLocationReview { get; init; }
    public ArrivalCandidate? Arrival { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

/// <summary>Flight tracking only. It cannot approve or dispatch a ground service.</summary>
public static class DutyTracking
{
    public static void Validate(DutyProgress duty)
    {
        if (duty.Id == Guid.Empty || !Enum.IsDefined(duty.State) || duty.Sectors.IsDefault ||
            duty.Sectors.Any(sector => sector is null || sector.Dispatcher is null || sector.Flight is null) ||
            duty.Sectors.Select(sector => sector.SessionId).Distinct().Count() != duty.Sectors.Length ||
            duty.Sectors.Select(sector => sector.Id).Distinct().Count() != duty.Sectors.Length ||
            duty.Sectors.Where((sector, index) => sector.Id == Guid.Empty || sector.SessionId == Guid.Empty || sector.Number != index + 1 ||
                sector.Dispatcher.SessionId != sector.SessionId || sector.PlanId == Guid.Empty || sector.ProfileRevision < 1).Any())
            throw new ArgumentException("Duty identities or sector history are invalid.");
    }
    public static DutyProgress Attach(DutyProgress? duty, GroundHandlingSession session, FlightPlanSnapshot flight, string aircraft, bool transitionConfirmed)
    {
        duty = duty is null || duty.State == DutyState.Ended ? new(Guid.NewGuid(), DutyState.Idle, []) : duty;
        if (duty.Sectors.Any(sector => sector.SessionId == session.Id)) return duty;
        if (duty.Sectors.Any(sector => sector.Id == session.SectorId || sector.PlanId == session.Plan.SnapshotId)) throw new InvalidOperationException("A new sector needs independent identities.");
        if (duty.Sectors.Length > 0 && !transitionConfirmed) throw new InvalidOperationException("Confirm the arrival airport and new sector before starting another operation.");
        if (duty.Sectors.LastOrDefault() is { Archived: false, HandlingCompleted: false }) throw new InvalidOperationException("Archive the current handling operation before changing sector.");
        var sector = new DutySector(session.SectorId, session.Id, duty.Sectors.Length + 1, flight, session.Dispatcher,
            session.Plan.SnapshotId, session.Plan.AirlineIcao, session.Plan.ProfileRevision, false, false);
        return duty with
        {
            State = DutyState.GroundHandling,
            Sectors = duty.Sectors.Add(sector),
            Aircraft = aircraft,
            DepartureObserved = false,
            AirborneSince = null,
            Arrival = null,
            RequiresLocationReview = false,
            LastObservedAt = null,
            Latitude = null,
            Longitude = null
        };
    }
    public static DutyProgress Complete(DutyProgress duty, Guid sessionId, bool completed, bool archived)
    {
        int index = -1;
        for (int i = 0; i < duty.Sectors.Length; i++) if (duty.Sectors[i].SessionId == sessionId) index = i;
        if (index < 0) throw new ArgumentException("Handling does not belong to this duty.");
        var sector = duty.Sectors[index];
        return duty with
        {
            Sectors = duty.Sectors.SetItem(index, sector with { HandlingCompleted = sector.HandlingCompleted || completed, Archived = sector.Archived || archived || completed }),
            State = completed && duty.State == DutyState.GroundHandling ? DutyState.ReleasedForFlight : !completed && archived && duty.State == DutyState.GroundHandling ? DutyState.AwaitingNextPlan : duty.State
        };
    }
    public static DutyProgress Observe(DutyProgress duty, DutyObservation observation)
    {
        if (duty.State == DutyState.Ended || duty.Sectors.IsEmpty || observation.At <= duty.LastObservedAt || !double.IsFinite(observation.Speed) || observation.Speed < 0) return duty;
        bool gap = duty.LastObservedAt is { } last && observation.At - last > TimeSpan.FromMinutes(2);
        bool teleport = duty.LastObservedAt is { } previousAt && duty.Latitude is { } lat && duty.Longitude is { } lon && observation.Latitude is { } newLat && observation.Longitude is { } newLon &&
            DistanceNm(lat, lon, newLat, newLon) > Math.Max(5, (observation.At - previousAt).TotalHours * 1200);
        var next = duty with { LastObservedAt = observation.At, Latitude = observation.Latitude, Longitude = observation.Longitude };
        if (duty.Aircraft != observation.Aircraft || gap || teleport)
            next = next with { RequiresLocationReview = true, State = DutyState.Paused, AirborneSince = null, Arrival = null };
        if (!observation.OnGround && observation.Speed >= 50 && !next.RequiresLocationReview)
        {
            next = next with { AirborneSince = next.AirborneSince ?? observation.At, Arrival = null };
            if (observation.At - next.AirborneSince >= TimeSpan.FromSeconds(5)) next = next with { DepartureObserved = true, State = DutyState.EnrouteObserved };
        }
        else if (observation.OnGround)
        {
            next = next with { AirborneSince = null };
            if (observation.Parked && observation.Speed < 0.5 && (next.DepartureObserved || next.RequiresLocationReview) && next.Arrival is null)
            {
                string reason = next.RequiresLocationReview ? "Location or simulator continuity needs confirmation" : observation.Airport == duty.Sectors[^1].Flight.Destination ? "Destination arrival observed" : "Diversion, return or unresolved airport";
                next = next with { State = DutyState.ArrivalCandidate, Arrival = new(observation.Airport, observation.Stand, reason, observation.At) };
            }
        }
        return next;
    }
    private static double DistanceNm(double a, double b, double c, double d)
    {
        double radians = Math.PI / 180;
        double value = Math.Pow(Math.Sin((c - a) * radians / 2), 2) + Math.Cos(a * radians) * Math.Cos(c * radians) * Math.Pow(Math.Sin((d - b) * radians / 2), 2);
        return 3440.065 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(value, 0, 1)));
    }
}
