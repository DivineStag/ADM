using System.Text.Json;
using System.Text.Json.Serialization;
using ADM.Domain;

namespace ADM.Application.Tests;

public sealed class ProfileJsonContractTests
{
    [Fact]
    public void Shipped_generic_fixture_matches_the_typed_in_memory_baseline()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "airline-default.json"));
        var actual = JsonSerializer.Deserialize<AirlineProfile>(json, options);
        Assert.NotNull(actual);
        var expected = SampleSessionFactory.GenericProfile();
        Assert.Equal(expected.Icao, actual.Icao);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Revision, actual.Revision);
        foreach (var (operation, policy) in expected.Operations)
        {
            Assert.Equal(6, actual.Operations[operation].Services.Count);
            foreach (var (kind, rule) in policy.Services.Where(pair => pair.Key != ServiceKind.Deboarding))
            {
                var loaded = actual.Operations[operation].Services[kind];
                Assert.Equal(rule.Policy, loaded.Policy);
                Assert.Equal(rule.Required, loaded.Required);
                Assert.Equal(rule.ApplicabilityRule, loaded.ApplicabilityRule);
                Assert.Empty(loaded.Dependencies);
            }
        }
    }
}
