using System.Text.Json;
using System.Text.Json.Nodes;
using ADM.Domain;
using ADM.Integrations.Gsx;

namespace ADM.Integration.ContractTests;

public sealed class GsxFlightReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static JsonObject Handler() => JsonNode.Parse("""
        {"aircraft":{"icaoType":"A20N","title":"Fixture aircraft","handlerName":"Fixture_Handler"},
         "simbrief":{"callsign":"BAW123","originICAO":"EGLL","destinationICAO":"EHAM","sched_out":[2026,10,21,13,0,0],
         "passengers":180,"plan_ramp":12345,"zfw":64000,"unitsName":"lbs","last_error":null}}
        """)!.AsObject();
    private static FlightPlanSnapshot? Read(JsonNode handler) { using var json = JsonDocument.Parse(handler.ToJsonString()); return GsxFlightReader.Read(json.RootElement, Now); }
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"title\":\"\",\"gen\":7}")]
    [InlineData("{\"title\":\"Question\",\"gen\":\"bad\"}")]
    public void Empty_or_malformed_prompt_state_is_not_a_question(string prompt)
    {
        Assert.False(GsxProtocol.Read("{\"v\":1,\"type\":\"patch\",\"ts\":1792584000,\"path\":\"/prompt\",\"value\":" + prompt + "}").PromptActive);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Documented_handler_fields_supply_a_validated_plan_and_planned_figures_without_account_or_loadsheet_claims(bool isoDate)
    {
        var handler = Handler(); if (isoDate) handler["simbrief"]!["sched_out"] = "2026-10-21T13:00:00Z";
        var plan = Read(handler); Assert.NotNull(plan); Assert.Equal(FlightPlanSource.Gsx, plan.Source);
        Assert.Equal("BAW123", plan.Callsign); Assert.Equal("EGLL", plan.Origin); Assert.Equal("EHAM", plan.Destination); Assert.Equal("A20N", plan.Aircraft.IcaoType);
        Assert.Equal(180, plan.PlannedPassengers); Assert.Equal(12345, plan.PlannedRampFuelKg); Assert.Equal(64000, plan.PlannedZeroFuelWeightKg);
        Assert.Equal(Now.AddHours(1), plan.ScheduledDepartureUtc); Assert.Equal("Fixture aircraft", plan.SourceAircraftTitle);
        using var json = JsonDocument.Parse(handler.ToJsonString()); Assert.Equal("A20N:Fixture_Handler", GsxFlightReader.SetupKey(json.RootElement));
    }
    [Theory]
    [InlineData("callsign", "")]
    [InlineData("originICAO", "BAD")]
    [InlineData("destinationICAO", "EGLL")]
    [InlineData("last_error", "Plan not loaded")]
    [InlineData("sched_out", "2020-01-01T12:00:00Z")]
    [InlineData("sched_out", "invalid")]
    public void Missing_invalid_stale_or_error_plans_fall_back_to_manual_entry(string field, string value)
    {
        var handler = Handler(); handler["simbrief"]![field] = value; Assert.Null(Read(handler));
    }
    [Fact]
    public void Missing_handler_context_and_malformed_load_values_are_not_invented()
    {
        Assert.Null(Read(new JsonObject()));
        var handler = Handler(); handler["simbrief"]!["passengers"] = -1; handler["simbrief"]!["plan_ramp"] = "unknown"; handler["simbrief"]!["zfw"] = null;
        var plan = Read(handler); Assert.NotNull(plan); Assert.Null(plan.PlannedPassengers); Assert.Null(plan.PlannedRampFuelKg); Assert.Null(plan.PlannedZeroFuelWeightKg);
    }
    [Fact]
    public void Protocol_exposes_coarse_handler_and_interaction_updates_without_new_commands()
    {
        var handler = GsxProtocol.Read("""{"v":1,"type":"patch","ts":1792584000,"path":"/handlerData","value":{"aircraft":{"icaoType":"A20N"}}}""");
        Assert.True(handler.HasHandlerData); Assert.Equal("A20N", GsxFlightReader.AircraftType(handler.HandlerData!.Value));
        Assert.True(GsxProtocol.Read("""{"v":1,"type":"patch","ts":1792584000,"path":"/menuShown","value":true}""").MenuShown);
        Assert.True(GsxProtocol.Read("""{"v":1,"type":"patch","ts":1792584000,"path":"/prompt","value":{"gen":7,"title":"Fuel choice"}}""").PromptActive);
        Assert.False(GsxProtocol.Read("""{"v":1,"type":"patch","ts":1792584000,"path":"/prompt","value":null}""").PromptActive);
    }
}
