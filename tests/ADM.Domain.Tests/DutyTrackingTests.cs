using ADM.Domain;

namespace ADM.Domain.Tests;

public sealed class DutyTrackingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static DutyProgress Released()
    {
        var sector = new DutySector(Guid.NewGuid(), Guid.NewGuid(), 1, new("BAW123", "EGLL", "EHAM", new("A20N", null), Now),
            new(Guid.Empty, new("sarah", "Sarah", "WarmProfessional", "en-GB"), Now), Guid.NewGuid(), "BAW", 2, true, true);
        sector = sector with { Dispatcher = sector.Dispatcher with { SessionId = sector.SessionId } };
        return new(Guid.NewGuid(), DutyState.ReleasedForFlight, [sector]) { Aircraft = "Test aircraft" };
    }
    private static DutyObservation Sample(int seconds, bool ground = true, double speed = 0, string? airport = "EGLL") =>
        new(Now.AddSeconds(seconds), ground, speed, ground, "Test aircraft", airport, ground ? "Stand 1" : null);
    [Fact]
    public void Wheels_up_requires_two_separated_airborne_samples_and_never_starts_services()
    {
        var duty = Released(); duty = DutyTracking.Observe(duty, Sample(0));
        duty = DutyTracking.Observe(duty, Sample(1, false, 150)); Assert.False(duty.DepartureObserved);
        duty = DutyTracking.Observe(duty, Sample(3, true)); Assert.False(duty.DepartureObserved);
        duty = DutyTracking.Observe(duty, Sample(4, false, 150));
        duty = DutyTracking.Observe(duty, Sample(9, false, 170));
        Assert.Equal(DutyState.EnrouteObserved, duty.State); Assert.True(duty.DepartureObserved); Assert.Single(duty.Sectors);
    }
    [Theory]
    [InlineData("EHAM")]
    [InlineData("EGLL")]
    [InlineData("LFPG")]
    [InlineData(null)]
    public void Destination_diversion_return_and_unresolved_arrivals_are_candidates_only(string? airport)
    {
        var duty = DutyTracking.Observe(Released(), Sample(0, false, 150));
        duty = DutyTracking.Observe(duty, Sample(6, false, 170));
        duty = DutyTracking.Observe(duty, Sample(60, airport: airport));
        Assert.Equal(DutyState.ArrivalCandidate, duty.State); Assert.Equal(airport, duty.Arrival!.Airport);
        var again = DutyTracking.Observe(duty, Sample(61, airport: airport)); Assert.Equal(duty.Arrival, again.Arrival); Assert.Single(again.Sectors);
    }
    [Fact]
    public void Startup_parked_is_not_arrival_but_a_recovered_location_requires_review()
    {
        var duty = DutyTracking.Observe(Released(), Sample(0, airport: "EHAM")); Assert.Null(duty.Arrival);
        duty = DutyTracking.Observe(duty with { RequiresLocationReview = true }, Sample(1, airport: "EHAM"));
        Assert.NotNull(duty.Arrival); Assert.Contains("confirmation", duty.Arrival.Reason);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Teleport_and_aircraft_changes_pause_continuity(bool teleport)
    {
        var duty = DutyTracking.Observe(Released(), Sample(0) with { Latitude = 51, Longitude = 0 });
        duty = DutyTracking.Observe(duty, Sample(1, false, 150) with { Aircraft = teleport ? "Test aircraft" : "Another aircraft", Latitude = teleport ? 40 : 51, Longitude = 0 });
        Assert.Equal(DutyState.Paused, duty.State); Assert.True(duty.RequiresLocationReview); Assert.False(duty.DepartureObserved);
    }
    [Fact]
    public void First_observation_after_recovery_establishes_a_position_baseline_when_saved_coordinates_have_no_timestamp()
    {
        var duty = Released() with { Latitude = 51, Longitude = 0, LastObservedAt = null, RequiresLocationReview = true };
        var observation = Sample(0) with { Latitude = 52, Longitude = 4 };
        var recovered = DutyTracking.Observe(duty, observation);
        Assert.Equal(observation.At, recovered.LastObservedAt);
        Assert.Equal(observation.Latitude, recovered.Latitude);
        Assert.True(recovered.RequiresLocationReview);
        Assert.False(recovered.DepartureObserved);
        Assert.Equal(DutyState.ArrivalCandidate, recovered.State);
        Assert.Single(recovered.Sectors);
    }
    [Fact]
    public void Gaps_and_out_of_order_samples_cannot_fabricate_a_departure()
    {
        var duty = DutyTracking.Observe(Released(), Sample(10, false, 150));
        Assert.Same(duty, DutyTracking.Observe(duty, Sample(9, false, 150)));
        duty = DutyTracking.Observe(duty, Sample(200, false, 150)); Assert.True(duty.RequiresLocationReview); Assert.False(duty.DepartureObserved);
    }
}
