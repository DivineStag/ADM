using ADM.Domain;

namespace ADM.Application.Tests;

public sealed class AirlineIdentityTests
{
    private readonly AirlineIdentityResolver _resolver = new(["BAW", "RYR", "UAL"]);

    [Theory]
    [InlineData("BAW482", "BAW")]
    [InlineData(" baw 7ab ", "BAW")]
    [InlineData("RYR123", "RYR")]
    [InlineData("UAL42", "UAL")]
    [InlineData("BA482", "GENERIC")]
    [InlineData("XYZ123", "GENERIC")]
    [InlineData("G-ABCD", "GENERIC")]
    [InlineData("N123AB", "GENERIC")]
    [InlineData("BAW", "GENERIC")]
    [InlineData("BAWTEST", "GENERIC")]
    public void Only_validated_icao_callsigns_resolve(string callsign, string expected)
    {
        Assert.Equal(expected, _resolver.Resolve(new(callsign, null, null, false), null).Icao);
    }

    [Fact]
    public void Precedence_exposes_source_conflict_and_allows_explicit_override()
    {
        var identity = new FlightIdentity("BAW482", "RYR123", "UAL", true);
        var result = _resolver.Resolve(identity, null);
        Assert.Equal("BAW", result.Icao);
        Assert.True(result.HasSourceConflict);
        Assert.Equal(AirlineSource.SimBriefAtcCallsign, result.Source);
        var manual = _resolver.Resolve(identity, new("UAL"));
        Assert.Equal("UAL", manual.Icao);
        Assert.Equal(AirlineSource.FlightOverride, manual.Source);
    }

    [Fact]
    public void Unverified_simulator_callsign_is_ignored()
    {
        var result = _resolver.Resolve(new(null, "RYR123", "BAW", false), null);
        Assert.Equal(AirlineSource.ManualSelection, result.Source);
        Assert.Equal("BAW", result.Icao);
    }

    [Fact]
    public void Invalid_explicit_override_is_reported()
    {
        Assert.Throws<ArgumentException>(() => _resolver.Resolve(new(null, null, null, false), new("XYZ")));
    }
}
