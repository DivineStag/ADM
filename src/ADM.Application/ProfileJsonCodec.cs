using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ADM.Domain;

namespace ADM.Application;

public sealed record ProfileLoadResult(AirlineProfile Profile, bool UsedFallback, string? Diagnostic);

/// <summary>Strict versioned profile validation shared by the editor and atomic JSON repository.</summary>
public static partial class ProfileJsonCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };
    public static AirlineProfile Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicates(document.RootElement);
        ValidateShape(document.RootElement);
        var profile = JsonSerializer.Deserialize<AirlineProfile>(json, Options) ?? throw new ArgumentException("Profile is empty.");
        profile = RestoreLegacySlot(profile);
        Validate(profile); return profile;
    }
    public static string Serialize(AirlineProfile profile)
    {
        Validate(profile);
        var node = JsonSerializer.SerializeToNode(profile, Options)!;
        RemoveLegacy(node);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    public static ProfileLoadResult Load(string json, AirlineProfile? lastValid, AirlineProfile generic)
    {
        Validate(generic);
        if (lastValid is not null) Validate(lastValid);
        try { return new(Parse(json), false, null); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        { return new(lastValid ?? generic, true, "Profile rejected; using the last valid revision or generic defaults. " + exception.Message); }
    }
    public static void Validate(AirlineProfile profile)
    {
        profile.Realism?.Validate();
        if (profile.SchemaVersion != 1 || profile.Revision < 1 || profile.Icao is null || !Icao().IsMatch(profile.Icao) ||
            string.IsNullOrWhiteSpace(profile.DisplayName) || profile.DisplayName.Length > 100 || profile.Operations is null ||
            profile.FamilyOverrides is null || profile.TypeOverrides is null)
            throw new ArgumentException("Invalid profile schema, ICAO identity, revision or required fields.");
        ValidateOperations(profile.Operations);
        foreach (var pair in profile.FamilyOverrides)
        {
            if (!Family().IsMatch(pair.Key) || pair.Value.CanonicalIdentity != pair.Key) throw new ArgumentException("Invalid canonical family override.");
            ValidateOperations(pair.Value.Operations);
        }
        foreach (var pair in profile.TypeOverrides)
        {
            if (!AircraftType().IsMatch(pair.Key) || pair.Value.CanonicalIdentity != pair.Key) throw new ArgumentException("Use canonical ICAO aircraft type overrides, not marketing titles.");
            ValidateOperations(pair.Value.Operations);
        }
        if (profile.Icao == "GENERIC" && Enum.GetValues<FlightOperationType>().Any(operation => !profile.Operations.TryGetValue(operation, out var policy) || Enum.GetValues<ServiceKind>().Where(kind => kind != ServiceKind.Deboarding).Any(kind => !policy.Services.ContainsKey(kind))))
            throw new ArgumentException("Generic profiles must explicitly cover every operation and service.");
        void ValidateOperations(ImmutableDictionary<FlightOperationType, OperationPolicy> operations)
        {
            if (operations is null) throw new ArgumentException("Operations must be an object.");
            foreach (var pair in operations)
            {
                if (!Enum.IsDefined(pair.Key) || pair.Value?.Services is null) throw new ArgumentException("Unknown operation or missing service map.");
                foreach (var service in pair.Value.Services)
                {
                    if (!Enum.IsDefined(service.Key) || service.Value is null || !Enum.IsDefined(service.Value.Policy) || !Enum.IsDefined(service.Value.Applicability) ||
                        service.Value.Dependencies is null || service.Value.Dependencies.Any(dependency => !Enum.IsDefined(dependency)) ||
                        string.IsNullOrWhiteSpace(service.Value.ApplicabilityRule)) throw new ArgumentException("Invalid typed service policy.");
                }
                // Validate explicit dependencies combined with mandatory default sequencing, even in partial airline layers.
                var rules = Enum.GetValues<ServiceKind>().ToImmutableDictionary(kind => kind,
                    kind => new ResolvedServiceRule(kind, pair.Value.Services.GetValueOrDefault(kind) ??
                        new ServiceRule(ServicePolicy.Disabled, false, [], "Validation baseline"), PolicyLayer.Airline, profile.Icao, profile.Revision));
                ServicePlanRules.Validate(new(Guid.Empty, profile.Icao, profile.Revision, new(null, null), pair.Key, rules, DateTimeOffset.UnixEpoch)
                { ServicePhases = pair.Value.ServicePhases ?? ServiceSequence.DefaultPhases });
            }
        }
    }
    private static AirlineProfile RestoreLegacySlot(AirlineProfile profile)
    {
        if (profile.Operations is null || profile.FamilyOverrides is null || profile.TypeOverrides is null) throw new ArgumentException("Profile maps are required.");
        ImmutableDictionary<FlightOperationType, OperationPolicy> Map(ImmutableDictionary<FlightOperationType, OperationPolicy> operations) => operations.ToImmutableDictionary(pair => pair.Key, pair =>
        {
            if (pair.Value?.Services is null || pair.Value.Services.Values.Any(rule => rule?.Dependencies is null)) throw new ArgumentException("Service maps and dependencies are required.");
            var services = pair.Value.Services.Remove(ServiceKind.Deboarding).ToImmutableDictionary(service => service.Key,
                service => service.Value with { Dependencies = service.Value.Dependencies.Remove(ServiceKind.Deboarding) });
            if (profile.Icao == "GENERIC") services = services.Add(ServiceKind.Deboarding, new(ServicePolicy.Disabled, false, [], "Removed from ADM handling"));
            return pair.Value with { Services = services, ServicePhases = pair.Value.ServicePhases?.SetItem(ServiceKind.Deboarding, 1) };
        });
        return profile with
        {
            Operations = Map(profile.Operations),
            FamilyOverrides = profile.FamilyOverrides.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Operations = Map(pair.Value.Operations) }),
            TypeOverrides = profile.TypeOverrides.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Operations = Map(pair.Value.Operations) })
        };
    }
    private static void RemoveLegacy(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("Deboarding");
            foreach (var child in obj.ToArray()) if (child.Value is not null) RemoveLegacy(child.Value);
        }
        else if (node is JsonArray array)
        {
            for (int i = array.Count - 1; i >= 0; i--) if (array[i] is JsonValue value && value.TryGetValue<string>(out var text) && text == "Deboarding") array.RemoveAt(i);
            foreach (var child in array) if (child is not null) RemoveLegacy(child);
        }
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate JSON property: " + property.Name);
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }
    private static void ValidateShape(JsonElement root)
    {
        Require(root, "schemaVersion", "icao", "displayName", "revision", "operations", "familyOverrides", "typeOverrides");
        CheckOperations(root.GetProperty("operations"));
        foreach (string layer in new[] { "familyOverrides", "typeOverrides" })
        {
            var overrides = root.GetProperty(layer);
            if (overrides.ValueKind != JsonValueKind.Object) throw new ArgumentException("Overrides must be objects.");
            foreach (var entry in overrides.EnumerateObject())
            { Require(entry.Value, "canonicalIdentity", "operations"); CheckOperations(entry.Value.GetProperty("operations")); }
        }
        static void CheckOperations(JsonElement operations)
        {
            if (operations.ValueKind != JsonValueKind.Object) throw new ArgumentException("Operations must be objects.");
            foreach (var operation in operations.EnumerateObject())
            {
                if (!Enum.GetNames<FlightOperationType>().Contains(operation.Name, StringComparer.Ordinal)) throw new ArgumentException("Unknown operation key.");
                Require(operation.Value, "services");
                var services = operation.Value.GetProperty("services");
                if (services.ValueKind != JsonValueKind.Object) throw new ArgumentException("Services must be objects.");
                foreach (var service in services.EnumerateObject())
                {
                    if (!Enum.GetNames<ServiceKind>().Contains(service.Name, StringComparer.Ordinal)) throw new ArgumentException("Unknown service key.");
                    Require(service.Value, "policy", "required", "dependencies", "applicabilityRule");
                    var dependencies = service.Value.GetProperty("dependencies");
                    if (dependencies.ValueKind != JsonValueKind.Array || dependencies.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
                        throw new ArgumentException("Dependencies must be service-name strings.");
                    string[] names = dependencies.EnumerateArray().Select(value => value.GetString()!).ToArray();
                    if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("Dependencies must be unique.");
                }
            }
        }
        static void Require(JsonElement element, params string[] properties)
        {
            if (element.ValueKind != JsonValueKind.Object || properties.Any(name => !element.TryGetProperty(name, out _)))
                throw new ArgumentException("Required profile fields are missing.");
        }
    }
    [GeneratedRegex("^(GENERIC|[A-Z]{3})$", RegexOptions.CultureInvariant)] private static partial Regex Icao();
    [GeneratedRegex("^[A-Z0-9_]{2,24}$", RegexOptions.CultureInvariant)] private static partial Regex Family();
    [GeneratedRegex("^[A-Z0-9]{2,4}$", RegexOptions.CultureInvariant)] private static partial Regex AircraftType();
}
