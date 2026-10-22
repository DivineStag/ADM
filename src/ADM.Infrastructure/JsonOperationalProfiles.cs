using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Infrastructure;

public sealed record AirportGateDocument(int SchemaVersion, ImmutableArray<AirportGateProfile> Airports);
public sealed class JsonAirportGateProfiles(string path, Action<string>? diagnostic = null) : IAirportGateProfiles
{
    public AirportGateProfile Resolve(string airportIcao)
    {
        if (!File.Exists(path)) return AirportGateProfile.Generic;
        try
        {
            if (new FileInfo(path).Length > 1_048_576) throw new ArgumentException("Airport profile file is too large.");
            var document = JsonSerializer.Deserialize<AirportGateDocument>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 });
            if (document is null || document.SchemaVersion != 1 || document.Airports.IsDefault || document.Airports.Any(item => item is null) ||
                document.Airports.Select(item => item.Icao).Distinct(StringComparer.Ordinal).Count() != document.Airports.Length) throw new ArgumentException("Invalid airport profile catalogue.");
            foreach (var item in document.Airports) item.Validate();
            return document.Airports.FirstOrDefault(item => item.Icao == airportIcao) ?? document.Airports.FirstOrDefault(item => item.Icao == "GENERIC") ?? AirportGateProfile.Generic;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { diagnostic?.Invoke("Airport gate profiles rejected; manual generic gate check retained. " + exception.Message); return AirportGateProfile.Generic; }
    }
}
public sealed class JsonProfileTransfer : IProfileTransfer
{
    public async Task<AirlineProfile> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > 1_048_576) throw new ArgumentException("Profile import is limited to 1 MiB.");
        return ADM.Application.ProfileJsonCodec.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
    }
    public async Task WriteAsync(string path, AirlineProfile profile, CancellationToken cancellationToken)
    {
        string json = ADM.Application.ProfileJsonCodec.Serialize(profile);
        string target = Path.GetFullPath(path), temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
