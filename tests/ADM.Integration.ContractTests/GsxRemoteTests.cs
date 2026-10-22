using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ADM.Contracts;
using ADM.Domain;
using ADM.Integrations.Gsx;

namespace ADM.Integration.ContractTests;

public sealed class GsxRemoteTests
{
    private const string Hello = """{"v":1,"type":"hello","ts":1792500000,"protocol":1,"engine":"couatl","sim":"MSFS","gsxRunning":true,"authRequired":false,"capabilities":["state","services","future-feature"]}""";
    private static string Services(string state = "available", int raw = 1, string id = "Water", bool trigger = true, string type = "snapshot") =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["type"] = type,
            ["ts"] = 1792500001,
            [type == "snapshot" ? "services" : "value"] = new[] { new { id, state, stateRaw = raw, canTrigger = trigger, waiting = false } },
            ["path"] = "/services"
        });
    private sealed class Socket : IGsxSocket
    {
        public Channel<string> Frames { get; } = Channel.CreateUnbounded<string>();
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool AutoAccept { get; set; } = true;
        public Uri? Endpoint { get; private set; }
        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) { Endpoint = endpoint; return Task.CompletedTask; }
        public Task SendAsync(string text, CancellationToken cancellationToken)
        {
            Sent.Enqueue(text);
            using var document = JsonDocument.Parse(text);
            if (AutoAccept && document.RootElement.TryGetProperty("id", out var id))
                Frames.Writer.TryWrite(JsonSerializer.Serialize(new { v = 1, type = "result", ts = 1792500002, id = id.GetString(), ok = true }));
            return Task.CompletedTask;
        }
        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            string text = await Frames.Reader.ReadAsync(cancellationToken);
            return text == "close" ? null : text;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static ServiceCommand Command(Guid session) => new(Guid.NewGuid(), session, ServiceKind.Water, 1, Guid.NewGuid(), "Contract test");
    private static async Task Until(Func<bool> condition)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, stop.Token);
    }
    private static Socket ReadySocket()
    {
        var socket = new Socket(); socket.Frames.Writer.TryWrite(Hello); socket.Frames.Writer.TryWrite(Services()); return socket;
    }
    private static GsxRemoteOptions Options => new() { CommandTimeout = TimeSpan.FromSeconds(2), ReconnectDelay = TimeSpan.FromMilliseconds(30) };

    [Theory]
    [InlineData("available", 1, ServiceState.Planned)]
    [InlineData("requested", 4, ServiceState.Requested)]
    [InlineData("performing", 5, ServiceState.Active)]
    [InlineData("completing", 7, ServiceState.Active)]
    [InlineData("completed", 6, ServiceState.Completed)]
    [InlineData("bypassed", 3, ServiceState.Unknown)]
    [InlineData("unavailable", 2, ServiceState.Unknown)]
    [InlineData("completed", 5, ServiceState.Unknown)]
    [InlineData("future", 99, ServiceState.Unknown)]
    public void Semantic_and_raw_states_must_agree_and_finishing_is_not_complete(string semantic, int raw, ServiceState expected)
    {
        var frame = GsxProtocol.Read(Services(semantic, raw));
        Assert.Equal(expected, Assert.Single(frame.Services!.Value).State);
    }
    [Fact]
    public void Pushback_is_never_exposed_and_unknown_additive_data_is_ignored()
    {
        Assert.Empty(GsxProtocol.Read(Services(id: "Departure")).Services!.Value);
        using var json = JsonDocument.Parse(GsxProtocol.Trigger(Guid.NewGuid(), ServiceKind.Refuelling));
        Assert.Equal("service.trigger", json.RootElement.GetProperty("verb").GetString());
        Assert.Equal("Refueling", json.RootElement.GetProperty("args").GetProperty("service").GetString());
        Assert.False(json.RootElement.TryGetProperty("v", out _));
        Assert.False(json.RootElement.TryGetProperty("ts", out _));
    }
    [Fact]
    public void Incompatible_protocol_or_remote_auth_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() => GsxProtocol.Read(Hello.Replace("\"protocol\":1", "\"protocol\":2", StringComparison.Ordinal)));
        Assert.Throws<NotSupportedException>(() => GsxProtocol.Read(Hello.Replace("\"v\":1", "\"v\":2", StringComparison.Ordinal)));
        Assert.Throws<NotSupportedException>(() => GsxProtocol.Read(Hello.Replace("\"authRequired\":false", "\"authRequired\":true", StringComparison.Ordinal)));
    }
    [Fact]
    public async Task Brief_completed_state_is_retained_for_the_worker_until_a_new_cycle_or_engine_reset()
    {
        var socket = ReadySocket();
        await using var gateway = new GsxRemoteGateway(Guid.NewGuid(), Options, () => socket);
        gateway.Start(); await Until(() => gateway.Connection == ConnectionState.Connected);
        socket.Frames.Writer.TryWrite(Services("performing", 5, trigger: false, type: "patch"));
        socket.Frames.Writer.TryWrite(Services("completed", 6, trigger: false, type: "patch"));
        socket.Frames.Writer.TryWrite(Services(type: "patch"));
        await Until(() => gateway.Snapshot.Services[ServiceKind.Water].State == ServiceState.Completed && gateway.Observations[ServiceKind.Water].State == ServiceState.Planned);
        Assert.Contains("completed/6", gateway.Snapshot.Services[ServiceKind.Water].Evidence.Reference);
        socket.Frames.Writer.TryWrite(Services("requested", 4, trigger: false, type: "patch"));
        await Until(() => gateway.Snapshot.Services[ServiceKind.Water].State == ServiceState.Requested);
        socket.Frames.Writer.TryWrite(Services(type: "patch"));
        await Until(() => gateway.Snapshot.Services[ServiceKind.Water].State == ServiceState.Planned);
    }
    [Fact]
    public void Boarding_uses_the_documented_service_trigger_and_has_no_departure_mapping()
    {
        using var document = JsonDocument.Parse(GsxProtocol.Trigger(Guid.NewGuid(), ServiceKind.Boarding));
        Assert.Equal("Boarding", document.RootElement.GetProperty("args").GetProperty("service").GetString());
        Assert.DoesNotContain("Departure", document.RootElement.GetRawText(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task Acceptance_is_not_completion_and_intent_is_sent_only_once()
    {
        var socket = ReadySocket(); Guid session = Guid.NewGuid();
        await using var gateway = new GsxRemoteGateway(session, Options, () => socket);
        gateway.Start(); await Until(() => gateway.Connection == ConnectionState.Connected);
        var capabilities = await gateway.ProbeAsync(CancellationToken.None);
        Assert.False(capabilities.IsFake); Assert.Equal("wire-1", capabilities.VerifiedApiVersion);
        Assert.Empty(capabilities.CancellableServices); Assert.Empty(capabilities.SupportedActions);
        var command = Command(session);
        var receipts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => gateway.RequestServiceAsync(command, CancellationToken.None)));
        Assert.All(receipts, receipt => Assert.Equal(ReceiptStatus.Accepted, receipt.Status));
        Assert.Equal(ServiceState.Planned, gateway.Observations[ServiceKind.Water].State);
        Assert.Single(socket.Sent, text => text.Contains("service.trigger", StringComparison.Ordinal));
        Assert.Equal(ReceiptStatus.Rejected, (await gateway.RequestServiceAsync(command with { CommandId = Guid.NewGuid() }, CancellationToken.None)).Status);
        socket.Frames.Writer.TryWrite(Services("performing", 5, trigger: false, type: "patch"));
        await Until(() => gateway.Observations[ServiceKind.Water].State == ServiceState.Active);
        socket.Frames.Writer.TryWrite(Services("completed", 6, trigger: false, type: "patch"));
        await Until(() => gateway.Observations[ServiceKind.Water].State == ServiceState.Completed);
    }
    [Fact]
    public async Task Unsupported_commands_and_unavailable_service_do_not_touch_the_wire()
    {
        var socket = ReadySocket(); Guid session = Guid.NewGuid();
        await using var gateway = new GsxRemoteGateway(session, Options, () => socket); gateway.Start();
        await Until(() => gateway.Connection == ConnectionState.Connected);
        var command = Command(session);
        Assert.Equal(ReceiptStatus.Rejected, (await gateway.CancelServiceAsync(command, CancellationToken.None)).Status);
        Assert.Equal(ReceiptStatus.Rejected, (await gateway.ExecuteVerifiedActionAsync(new(Guid.NewGuid(), session, GsxAction.CloseSupportedDoors), CancellationToken.None)).Status);
        Assert.Equal(ReceiptStatus.Rejected, (await gateway.RequestServiceAsync(command with { Kind = ServiceKind.Catering }, CancellationToken.None)).Status);
        Assert.Equal(ReceiptStatus.Rejected, (await gateway.RequestServiceAsync(command with { SessionId = Guid.NewGuid() }, CancellationToken.None)).Status);
        Assert.Single(socket.Sent); // Subscription only.
    }
    [Fact]
    public async Task Timeout_is_unknown_and_repeated_request_does_not_retry()
    {
        var socket = ReadySocket(); socket.AutoAccept = false; Guid session = Guid.NewGuid();
        await using var gateway = new GsxRemoteGateway(session, Options, () => socket); gateway.Start();
        await Until(() => gateway.Connection == ConnectionState.Connected);
        var command = Command(session);
        Assert.Equal(ReceiptStatus.Unknown, (await gateway.RequestServiceAsync(command, CancellationToken.None)).Status);
        Assert.Equal(ReceiptStatus.Unknown, (await gateway.RequestServiceAsync(command, CancellationToken.None)).Status);
        Assert.Equal(ReceiptStatus.Rejected, (await gateway.RequestServiceAsync(command with { CommandId = Guid.NewGuid(), IntentVersion = 2 }, CancellationToken.None)).Status);
        Assert.Single(socket.Sent, text => text.Contains("service.trigger", StringComparison.Ordinal));
    }
    [Fact]
    public async Task Reconnect_resubscribes_and_pending_command_is_never_replayed()
    {
        var first = ReadySocket(); first.AutoAccept = false;
        var second = ReadySocket(); int connections = 0; Guid session = Guid.NewGuid();
        await using var gateway = new GsxRemoteGateway(session, Options, () => Interlocked.Increment(ref connections) == 1 ? first : second);
        gateway.Start(); await Until(() => gateway.Connection == ConnectionState.Connected);
        var command = Command(session);
        var task = gateway.RequestServiceAsync(command, CancellationToken.None);
        await Until(() => first.Sent.Count == 2);
        first.Frames.Writer.TryWrite("close");
        Assert.Equal(ReceiptStatus.Unknown, (await task).Status);
        await Until(() => connections >= 2 && gateway.Connection == ConnectionState.Connected);
        Assert.Contains(GsxProtocol.Subscribe, second.Sent);
        Assert.Equal(ReceiptStatus.Unknown, (await gateway.RequestServiceAsync(command, CancellationToken.None)).Status);
        Assert.DoesNotContain(second.Sent, text => text.Contains("service.trigger", StringComparison.Ordinal));
    }
    [Fact]
    public async Task Engine_loss_clears_capabilities_and_publishes_source_qualified_unknown()
    {
        var socket = ReadySocket(); Guid session = Guid.NewGuid();
        await using var gateway = new GsxRemoteGateway(session, Options, () => socket);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = gateway.WatchAsync(stop.Token).GetAsyncEnumerator();
        gateway.Start(); Assert.True(await events.MoveNextAsync());
        Assert.Equal(EvidenceSource.Gsx, events.Current.Metadata.Source);
        Assert.Equal(session, events.Current.Metadata.SessionId);
        socket.Frames.Writer.TryWrite("""{"v":1,"type":"event","ts":1792500003,"topic":"engine","gsxRunning":false}""");
        Assert.True(await events.MoveNextAsync()); Assert.Equal(ServiceState.Unknown, events.Current.State);
        Assert.Empty((await gateway.ProbeAsync(stop.Token)).RequestableServices);
        socket.Frames.Writer.TryWrite("""{"v":1,"type":"event","ts":1792500004,"topic":"engine","gsxRunning":true}""");
        await Until(() => socket.Sent.Any(text => text.Contains("state.get", StringComparison.Ordinal)));
        Assert.Empty((await gateway.ProbeAsync(stop.Token)).RequestableServices);
        socket.Frames.Writer.TryWrite(Services());
        await Until(() => gateway.Connection == ConnectionState.Connected);
        Assert.Contains(ServiceKind.Water, (await gateway.ProbeAsync(stop.Token)).RequestableServices);
    }
    [Fact]
    public async Task Handler_plan_is_feature_detected_read_only_and_cleared_on_engine_loss()
    {
        var socket = new Socket(); Guid id = Guid.NewGuid();
        await using var gateway = new GsxRemoteGateway(id, new() { CommandTimeout = TimeSpan.FromSeconds(1) }, () => socket);
        gateway.Start(); socket.Frames.Writer.TryWrite(Hello.Replace("future-feature", "handlerData", StringComparison.Ordinal));
        var snapshot = System.Text.Json.Nodes.JsonNode.Parse(Services())!;
        snapshot["ts"] = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        snapshot["handlerData"] = System.Text.Json.Nodes.JsonNode.Parse("""{"aircraft":{"icaoType":"A20N","title":"Fixture","handlerName":"Fixture_Handler"},"simbrief":{"callsign":"BAW123","originICAO":"EGLL","destinationICAO":"EHAM","sched_out":[2026,10,21,13,0,0],"passengers":180,"zfw":64000}}""");
        snapshot["handlerData"]!["airport"] = System.Text.Json.Nodes.JsonNode.Parse("""{"icao":"EGLL"}""");
        snapshot["handlerData"]!["gate"] = System.Text.Json.Nodes.JsonNode.Parse("""{"uiName":"Gate A1"}""");
        socket.Frames.Writer.TryWrite(snapshot.ToJsonString()); await Until(() => gateway.Snapshot.FlightPlan is not null);
        Assert.Equal("BAW123", gateway.Snapshot.FlightPlan!.Callsign); Assert.Equal("A20N", gateway.Snapshot.AircraftIcaoType);
        Assert.Equal("A20N:Fixture_Handler", gateway.Snapshot.AircraftSetupKey);
        Assert.Equal("EGLL", gateway.Snapshot.AirportIcao); Assert.Equal("Gate A1", gateway.Snapshot.Stand);
        socket.Frames.Writer.TryWrite("""{"v":1,"type":"patch","ts":1792584001,"path":"/menuShown","value":true}""");
        await Until(() => gateway.Snapshot.MenuOpen);
        Assert.False(gateway.Snapshot.AwaitingInput);
        socket.Frames.Writer.TryWrite("""{"v":1,"type":"patch","ts":1792584001,"path":"/prompt","value":{"gen":7,"title":"Enter a value"}}""");
        await Until(() => gateway.Snapshot.AwaitingInput);
        socket.Frames.Writer.TryWrite("""{"v":1,"type":"event","ts":1792584002,"topic":"engine","gsxRunning":false}""");
        await Until(() => gateway.Snapshot.FlightPlan is null);
        Assert.Null(gateway.Snapshot.AircraftIcaoType); Assert.False(gateway.Snapshot.AwaitingInput);
        Assert.DoesNotContain(socket.Sent, message => message.Contains("service.trigger", StringComparison.Ordinal));
    }
    [Fact]
    public void Port_discovery_honours_section_and_rereads_edits_with_safe_fallback()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[other]\nremote_server_port=9000\n[gsx]\nremote_server_port = 9001 ; custom\n");
            Assert.Equal(9001, GsxPortDiscovery.Read(path));
            File.WriteAllText(path, "[gsx]\nremote_server_port=9002"); Assert.Equal(9002, GsxPortDiscovery.Read(path));
            File.WriteAllText(path, "[gsx]\nremote_server_port=99999"); Assert.Equal(8744, GsxPortDiscovery.Read(path));
        }
        finally { File.Delete(path); }
        Assert.Equal(8744, GsxPortDiscovery.Read(path));
    }
}
