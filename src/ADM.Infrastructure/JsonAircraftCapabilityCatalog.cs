using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADM.Application;
using ADM.Domain;

namespace ADM.Infrastructure;

public sealed record AircraftCapabilityDocument(int SchemaVersion, ImmutableArray<AircraftServiceCapabilities> Aircraft);
public sealed class JsonAircraftCapabilityCatalog(string path)
{
    public AircraftServiceCapabilities Resolve(string aircraftType)
    {
        if (File.Exists(path))
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
            var document = JsonSerializer.Deserialize<AircraftCapabilityDocument>(File.ReadAllText(path), options);
            if (document is null || document.SchemaVersion != 1 || document.Aircraft.IsDefault) throw new InvalidDataException("Aircraft capability matrix is invalid.");
            foreach (var profile in document.Aircraft)
            {
                if (CanonicalAircraftIdentity.Create(profile.IcaoType, null).IcaoType != profile.IcaoType || profile.Services is null || profile.ConcurrentGroups.IsDefault ||
                    profile.Services.Any(service => !Enum.IsDefined(service)) || string.IsNullOrWhiteSpace(profile.EvidenceReference) ||
                    profile.ConcurrentGroups.Any(group => group is null || group.Count < 2 || group.Contains(ServiceKind.Boarding) || !group.IsSubsetOf(profile.Services)))
                    throw new InvalidDataException("Aircraft capability matrix has an invalid type, service or concurrency group.");
            }
            if (document.Aircraft.Select(item => item.IcaoType).Distinct(StringComparer.Ordinal).Count() != document.Aircraft.Length)
                throw new InvalidDataException("Duplicate aircraft capability entries.");
            var selected = document.Aircraft.SingleOrDefault(profile => profile.IcaoType == aircraftType);
            if (selected is not null) return selected;
        }
        return new(aircraftType, Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], "Conservative fallback: GSX-advertised basic services only; no validated concurrent or release actions.");
    }
}
