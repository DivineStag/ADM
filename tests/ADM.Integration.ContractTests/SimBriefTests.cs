using System.Net;
using System.Text;
using ADM.Integrations.SimBrief;

namespace ADM.Integration.ContractTests;

public sealed class SimBriefTests
{
    private const string Ofp = """{"params":{"time_generated":"2026-10-20T09:19:13Z"},"atc":{"callsign":"BAW482"},"origin":{"icao_code":"EGLL"},"destination":{"icao_code":"EHAM"},"aircraft":{"icao_code":"A20N"},"future_field":true}""";
    private sealed class Handler(HttpStatusCode status = HttpStatusCode.OK, string json = Ofp) : HttpMessageHandler
    {
        public List<Uri> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls.Add(request.RequestUri!); return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }); }
    }
    [Fact]
    public async Task Explicit_import_uses_v2_client_parameter_and_caches_no_account_data_in_snapshot()
    {
        var handler = new Handler(); using var client = new HttpClient(handler); var importer = new SimBriefImporter(client);
        Assert.Empty(handler.Calls);
        var result = await importer.ImportLatestAsync("fixture_alias", CancellationToken.None);
        Assert.NotNull(result.Plan); Assert.Null(result.Error);
        Assert.Equal("BAW482", result.Plan.Callsign); Assert.Equal("EGLL", result.Plan.Origin); Assert.Equal("EHAM", result.Plan.Destination);
        Assert.Equal("A20N", result.Plan.Aircraft.IcaoType); Assert.Equal(DateTimeOffset.Parse("2026-10-20T09:19:13Z"), result.Plan.CreatedAt);
        Assert.Contains("username=fixture_alias", Assert.Single(handler.Calls).Query); Assert.Contains("json=v2", handler.Calls[0].Query);
        Assert.Contains("client=ADM", handler.Calls[0].Query);
    }
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, Ofp)]
    [InlineData(HttpStatusCode.OK, "<error>invalid user</error>")]
    [InlineData(HttpStatusCode.OK, "{\"atc\":{}}")]
    public async Task Errors_preserve_existing_plan_by_returning_no_replacement(HttpStatusCode status, string json)
    {
        var handler = new Handler(status, json); using var client = new HttpClient(handler);
        var result = await new SimBriefImporter(client).ImportLatestAsync("12345", CancellationToken.None);
        Assert.Null(result.Plan); Assert.NotNull(result.Error); Assert.Contains("userid=12345", Assert.Single(handler.Calls).Query);
        Assert.DoesNotContain("12345", result.Error);
    }
    [Fact]
    public async Task Invalid_input_and_cancelled_requests_do_not_make_a_network_call()
    {
        var handler = new Handler(); using var client = new HttpClient(handler); var importer = new SimBriefImporter(client);
        Assert.Null((await importer.ImportLatestAsync("", CancellationToken.None)).Plan); Assert.Empty(handler.Calls);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportLatestAsync("fixture", stop.Token));
    }
}
