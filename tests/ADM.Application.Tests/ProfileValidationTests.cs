using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADM.Domain;

namespace ADM.Application.Tests;

public sealed class ProfileValidationTests
{
    private static string ValidJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(SampleSessionFactory.GenericProfile(), options);
    }
    [Fact]
    public void Explicit_generic_override_wins_without_claiming_a_validated_carrier()
    {
        var resolver = new AirlineIdentityResolver(["BAW"]);
        var result = resolver.Resolve(new("BAW482", null, null, false), new("GENERIC"));
        Assert.Equal("GENERIC", result.Icao);
        Assert.Equal(AirlineSource.FlightOverride, result.Source);
        Assert.False(result.IsValidated);
        Assert.Null(result.ParsedDesignator);
    }

    [Fact]
    public void Required_json_members_are_enforced_before_default_values_can_hide_omissions()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(ValidJson())!;
        var service = root["operations"]!["Turnaround"]!["services"]!["Catering"]!.AsObject();
        service.Remove("policy");
        Assert.Throws<ArgumentException>(() => ProfileJsonCodec.Parse(root.ToJsonString()));
    }

    [Fact]
    public void Strict_json_roundtrip_retains_every_operation_and_rule()
    {
        var profile = ProfileJsonCodec.Parse(ValidJson());
        Assert.Equal(3, profile.Operations.Count);
        Assert.All(profile.Operations.Values, operation => Assert.Equal(7, operation.Services.Count));
    }
    [Theory]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("invalid")]
    public void Damaged_json_preserves_last_valid_profile_with_visible_diagnostic(string invalid)
    {
        var generic = SampleSessionFactory.GenericProfile();
        var last = generic with { Icao = "BAW", Revision = 7 };
        var result = ProfileJsonCodec.Load(invalid, last, generic);
        Assert.Same(last, result.Profile);
        Assert.True(result.UsedFallback);
        Assert.NotNull(result.Diagnostic);
        Assert.Same(generic, ProfileJsonCodec.Load(invalid, null, generic).Profile);
    }
    [Theory]
    [InlineData("\"Automatic\"", "\"UnverifiedMode\"")]
    [InlineData("\"Automatic\"", "99")]
    public void Unknown_enum_policies_are_rejected(string from, string to) =>
        Assert.Throws<JsonException>(() => ProfileJsonCodec.Parse(ValidJson().Replace(from, to, StringComparison.Ordinal)));

    [Fact]
    public void Profile_dependency_cycles_and_marketing_aircraft_names_are_rejected()
    {
        var profile = SampleSessionFactory.GenericProfile();
        var operation = profile.Operations[FlightOperationType.Turnaround];
        var modified = operation.Services[ServiceKind.Deboarding] with { Dependencies = [ServiceKind.Cleaning] };
        Assert.Throws<ArgumentException>(() => ProfileJsonCodec.Validate(profile with
        {
            Operations = profile.Operations.SetItem(FlightOperationType.Turnaround,
            operation with { Services = operation.Services.SetItem(ServiceKind.Deboarding, modified) })
        }));
        Assert.Throws<ArgumentException>(() => ProfileJsonCodec.Validate(profile with
        {
            TypeOverrides = profile.TypeOverrides.Add("Airbus A320neo",
            new("Airbus A320neo", ImmutableDictionary<FlightOperationType, OperationPolicy>.Empty))
        }));
    }
    [Fact]
    public void Explicit_alias_can_map_a_validated_callsign_prefix_without_guessing()
    {
        var resolver = new AirlineIdentityResolver(["BAW"], new Dictionary<string, string> { ["ZZZ"] = "BAW" });
        var result = resolver.Resolve(new("ZZZ123", null, null, false), null);
        Assert.Equal("BAW", result.Icao);
        Assert.Equal("ZZZ", result.ParsedDesignator);
        Assert.Equal("GENERIC", resolver.Resolve(new("XYZ123", null, null, false), null).Icao);
    }
}
