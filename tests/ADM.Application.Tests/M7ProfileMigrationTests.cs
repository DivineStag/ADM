using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application.Tests;

public sealed class M7ProfileMigrationTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    [Fact]
    public void Legacy_deboarding_profiles_remain_readable_but_cannot_enable_deboarding_and_exports_omit_it()
    {
        var profile = SampleSessionFactory.GenericProfile();
        var operation = profile.Operations[FlightOperationType.Turnaround];
        profile = profile with
        {
            Operations = profile.Operations.SetItem(FlightOperationType.Turnaround, operation with
            { Services = operation.Services.SetItem(ServiceKind.Deboarding, new(ServicePolicy.Automatic, false, [], "Legacy service")) })
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new JsonStringEnumConverter());
        var parsed = ProfileJsonCodec.Parse(JsonSerializer.Serialize(profile, options));
        Assert.Equal(ServicePolicy.Disabled, parsed.Operations[FlightOperationType.Turnaround].Services[ServiceKind.Deboarding].Policy);
        string exported = ProfileJsonCodec.Serialize(parsed); Assert.DoesNotContain("Deboarding", exported, StringComparison.Ordinal);
        var roundtrip = ProfileJsonCodec.Parse(exported);
        foreach (var operationType in Enum.GetValues<FlightOperationType>())
        {
            var plan = new FoundationServicePlanResolver(new Clock()).Resolve(roundtrip, null, new("A20N", null), operationType,
                new(ImmutableDictionary<ServiceKind, ServiceRule>.Empty.Add(ServiceKind.Deboarding, new(ServicePolicy.Automatic, false, [], "Attempted override"))));
            Assert.Equal(ServicePolicy.Disabled, plan.Services[ServiceKind.Deboarding].Rule.Policy);
        }
    }
}
