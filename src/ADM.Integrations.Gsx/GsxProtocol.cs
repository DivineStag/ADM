using System.Collections.Immutable;
using System.Text.Json;
using ADM.Domain;

namespace ADM.Integrations.Gsx;

public sealed record RemoteServiceObservation(ServiceKind Kind, string Id, string SemanticState, int StateRaw,
    ServiceState State, bool CanTrigger, bool Waiting, DateTimeOffset ObservedAt);
public sealed record RemoteFrame(string Type, DateTimeOffset ObservedAt, bool? Running = null,
    ImmutableHashSet<string>? Capabilities = null, ImmutableArray<RemoteServiceObservation>? Services = null,
    string? ResultId = null, bool? Ok = null, string? Error = null)
{
    public ImmutableDictionary<string, string?> Context { get; init; } = ImmutableDictionary<string, string?>.Empty;
    public JsonElement? HandlerData { get; init; }
    public bool HasHandlerData { get; init; }
    public bool? MenuShown { get; init; }
    public bool? PromptActive { get; init; }
}

/// <summary>Installed GSX manual pp.269–272,279–281,291: Remote API v2 / wire protocol 1.</summary>
public static class GsxProtocol
{
    private static readonly ImmutableDictionary<string, ServiceKind> Services = new Dictionary<string, ServiceKind>(StringComparer.Ordinal)
    {
        ["Cleaning"] = ServiceKind.Cleaning,
        ["Water"] = ServiceKind.Water,
        ["Lavatory"] = ServiceKind.Lavatory,
        ["Refueling"] = ServiceKind.Refuelling,
        ["Catering"] = ServiceKind.Catering,
        ["Boarding"] = ServiceKind.Boarding
    }.ToImmutableDictionary(StringComparer.Ordinal);
    public const string Subscribe = "{\"type\":\"subscribe\",\"channels\":[\"state\"]}";
    public static string Trigger(Guid id, ServiceKind kind)
    {
        string service = Services.Single(pair => pair.Value == kind).Key;
        return JsonSerializer.Serialize(new { type = "command", id = id.ToString("D"), verb = "service.trigger", args = new { service } });
    }

    private static bool HasPrompt(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("gen", out var generation) && generation.ValueKind == JsonValueKind.Number && generation.TryGetInt64(out _) &&
        value.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(title.GetString());
    public static RemoteFrame Read(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.GetProperty("v").GetInt32() != 1) throw new NotSupportedException("Unsupported GSX wire protocol; controls disabled.");
        string type = root.GetProperty("type").GetString() ?? throw new JsonException("Missing frame type.");
        var observed = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("ts").GetInt64());
        if (type == "hello")
        {
            if (root.GetProperty("protocol").GetInt32() != 1 || root.GetProperty("engine").GetString() != "couatl" || root.GetProperty("sim").GetString() != "MSFS" ||
                root.GetProperty("authRequired").GetBoolean()) throw new NotSupportedException("Unsupported engine, protocol or authentication requirement.");
            return new(type, observed, root.GetProperty("gsxRunning").GetBoolean(),
                root.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString() ?? "").ToImmutableHashSet(StringComparer.Ordinal));
        }
        if (type == "event" && root.GetProperty("topic").GetString() == "engine")
            return new(type, observed, root.GetProperty("gsxRunning").GetBoolean());
        if (type == "result") return new(type, observed,
            ResultId: root.TryGetProperty("id", out var id) ? id.GetString() : null, Ok: root.GetProperty("ok").GetBoolean(),
            Error: root.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null);
        if (type == "snapshot")
            return new(type, observed, Services: root.TryGetProperty("services", out var snapshot) ? ReadServices(snapshot, observed) : [])
            {
                HandlerData = root.TryGetProperty("handlerData", out var handler) ? handler.Clone() : null,
                HasHandlerData = root.TryGetProperty("handlerData", out _),
                MenuShown = root.TryGetProperty("menuShown", out var shown) && shown.ValueKind is JsonValueKind.True or JsonValueKind.False ? shown.GetBoolean() : false,
                PromptActive = root.TryGetProperty("prompt", out var prompt) && HasPrompt(prompt),
                Context = new[] { "airport", "parking", "aircraft" }.ToImmutableDictionary(key => key,
                key => root.TryGetProperty(key, out var item) ? ContextValue(key, item) : null)
            };
        if (type == "patch" && root.GetProperty("path").GetString() == "/menuShown")
            return new(type, observed) { MenuShown = root.GetProperty("value").GetBoolean() };
        if (type == "patch" && root.GetProperty("path").GetString() == "/prompt")
            return new(type, observed) { PromptActive = HasPrompt(root.GetProperty("value")) };
        if (type == "patch" && root.GetProperty("path").GetString() == "/handlerData")
            return new(type, observed) { HandlerData = root.GetProperty("value").Clone(), HasHandlerData = true };
        if (type == "patch" && root.GetProperty("path").GetString() == "/services")
            return new(type, observed, Services: ReadServices(root.GetProperty("value"), observed));
        if (type == "patch" && root.GetProperty("path").GetString() is "/airport" or "/parking" or "/aircraft")
        {
            string key = root.GetProperty("path").GetString()![1..];
            return new(type, observed) { Context = ImmutableDictionary<string, string?>.Empty.Add(key, ContextValue(key, root.GetProperty("value"))) };
        }
        return new(type, observed); // Unknown additive keys/topics are ignored within protocol 1.
    }
    private static string? ContextValue(string key, JsonElement value)
    {
        if (key == "airport" && value.ValueKind == JsonValueKind.Object)
            return value.TryGetProperty("icao", out var icao) && icao.ValueKind == JsonValueKind.String ? icao.GetString() : null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static ImmutableArray<RemoteServiceObservation> ReadServices(JsonElement value, DateTimeOffset observed)
    {
        if (value.ValueKind == JsonValueKind.Null) return [];
        var result = ImmutableArray.CreateBuilder<RemoteServiceObservation>();
        var seen = new HashSet<ServiceKind>();
        foreach (var service in value.EnumerateArray())
        {
            string id = service.GetProperty("id").GetString() ?? "";
            if (!Services.TryGetValue(id, out var kind)) continue; // Includes Departure: never exposed as a command.
            if (!seen.Add(kind)) throw new JsonException("Duplicate service in GSX state array.");
            string semantic = service.GetProperty("state").GetString() ?? "unknown";
            int raw = service.GetProperty("stateRaw").GetInt32();
            ServiceState state = (semantic, raw) switch
            {
                ("available", 1) => ServiceState.Planned,
                ("requested", 4) => ServiceState.Requested,
                ("performing", 5) or ("completing", 7) => ServiceState.Active,
                ("completed", 6) => ServiceState.Completed,
                _ => ServiceState.Unknown // unavailable/bypassed never prove authorized skip or cancellation.
            };
            result.Add(new(kind, id, semantic, raw, state,
                semantic == "available" && raw == 1 && service.GetProperty("canTrigger").GetBoolean(),
                service.TryGetProperty("waiting", out var waiting) && waiting.GetBoolean(), observed));
        }
        return result.ToImmutable();
    }
}
