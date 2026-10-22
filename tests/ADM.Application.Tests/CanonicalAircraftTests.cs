namespace ADM.Application.Tests;

public sealed class CanonicalAircraftTests
{
    [Fact]
    public void Canonical_designators_normalize_without_guessing_marketing_titles()
    {
        Assert.Equal("A20N", CanonicalAircraftIdentity.Create(" a20n ", "a320_family").IcaoType);
        Assert.Equal("A320_FAMILY", CanonicalAircraftIdentity.Create(null, "a320_family").Family);
        Assert.Null(CanonicalAircraftIdentity.Create("Airbus A320neo", null).IcaoType);
        var mapping = new Dictionary<string, string> { ["Airbus A320neo"] = "A20N" };
        Assert.Equal("A20N", CanonicalAircraftIdentity.Create("Airbus A320neo", null, mapping).IcaoType);
    }
}
