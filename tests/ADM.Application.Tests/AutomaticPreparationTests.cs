using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application.Tests;

public sealed class AutomaticPreparationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static FlightPlanSnapshot Flight => new("BAW123", "EGLL", "EHAM", new("A20N", null), Now) { Source = FlightPlanSource.Gsx, SourceAircraftTitle = "Fixture" };
    private static SimulatorSnapshot Sim => new(Now, ConnectionState.Connected, true, 0, true, false, "Fixture", null, null, null, EvidenceSource.SimConnect);
    private static GsxLiveSnapshot Gsx => new(ConnectionState.Connected, 1, Now, ImmutableDictionary<ServiceKind, GsxServiceStatus>.Empty, "EGLL", "A1", "Fixture") { AircraftIcaoType = "A20N" };
    [Fact]
    public void GSX_lowercase_aircraft_title_matches_the_SDK_title_but_a_different_title_still_blocks_preparation()
    {
        Assert.True(AutomaticPreparation.Check(Flight with { SourceAircraftTitle = " 737-800 pax ssw hd " }, Sim with { AircraftTitle = "737-800 PAX SSW HD" }, Gsx, true, Now).Ready);
        Assert.False(AutomaticPreparation.Check(Flight with { SourceAircraftTitle = "737-800 pax ssw hd" }, Sim with { AircraftTitle = "Different aircraft" }, Gsx, true, Now).Ready);
    }
    [Fact]
    public void Configured_matching_flight_prepares_without_per_flight_checkboxes_but_no_plan_or_setup_still_waits()
    {
        Assert.True(AutomaticPreparation.Check(Flight, Sim, Gsx, true, Now).Ready);
        Assert.False(AutomaticPreparation.Check(Flight, Sim, Gsx, false, Now).Ready);
        Assert.Contains("setup", AutomaticPreparation.Check(Flight, Sim, Gsx, false, Now).Message);
        Assert.False(AutomaticPreparation.Check(null, Sim, Gsx, true, Now).Ready);
        Assert.Contains("flight plan", AutomaticPreparation.Check(null, Sim, Gsx, true, Now).Message);
    }
    [Theory]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("aircraft")]
    [InlineData("type")]
    [InlineData("airport")]
    [InlineData("input")]
    [InlineData("offline")]
    public void Automatic_preparation_still_requires_current_matching_aircraft_airport_and_connections(string condition)
    {
        var sim = condition switch
        {
            "airborne" => Sim with { OnGround = false },
            "moving" => Sim with { GroundSpeedKnots = 4 },
            "brake" => Sim with { ParkingBrakeSet = false },
            "engines" => Sim with { EnginesRunning = true },
            "stale" => Sim with { ObservedAt = Now.AddMinutes(-1) },
            "future" => Sim with { ObservedAt = Now.AddMinutes(1) },
            "aircraft" => Sim with { AircraftTitle = "Different aircraft" },
            "offline" => Sim with { Connection = ConnectionState.Offline },
            _ => Sim
        };
        var gsx = condition switch { "type" => Gsx with { AircraftIcaoType = "B738" }, "airport" => Gsx with { AirportIcao = "EHAM" }, "input" => Gsx with { AwaitingInput = true }, _ => Gsx };
        Assert.False(AutomaticPreparation.Check(Flight, sim, gsx, true, Now).Ready);
    }
    [Theory]
    [InlineData("airborne")]
    [InlineData("moving")]
    [InlineData("brake")]
    [InlineData("engines")]
    [InlineData("unknown")]
    public void Cockpit_state_does_not_duplicate_GSX_service_checks(string condition)
    {
        var sim = condition switch
        {
            "airborne" => Sim with { OnGround = false },
            "moving" => Sim with { GroundSpeedKnots = 12 },
            "brake" => Sim with { ParkingBrakeSet = false },
            "engines" => Sim with { EnginesRunning = true },
            _ => Sim with { OnGround = null, GroundSpeedKnots = null, ParkingBrakeSet = null, EnginesRunning = null }
        };
        Assert.True(AutomaticPreparation.Check(Flight, sim, Gsx, true, Now).Ready);
    }
}
