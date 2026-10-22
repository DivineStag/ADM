using ADM.Application;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Infrastructure;

/// <summary>Validated optimistic revisions with atomic replacement and one rollback revision.</summary>
public sealed class JsonAirlineProfileRepository(string directory, Action<string>? diagnostic = null, string? bundledDirectory = null) : IAirlineProfileRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<IReadOnlyList<AirlineProfile>> ListAsync(CancellationToken cancellationToken)
    {
        var profiles = new List<AirlineProfile>();
        var keys = new[] { directory, bundledDirectory }.Where(path => path is not null && Directory.Exists(path))
            .SelectMany(path => Directory.EnumerateFiles(path!, "*.json*"))
            .Select(Path.GetFileName).Where(name => name is not null && (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)))
            .Select(name => name!.Split('.')[0]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (key != "GENERIC" && (key.Length != 3 || key.Any(character => character is < 'A' or > 'Z'))) continue;
            if (await FindAsync(key, cancellationToken).ConfigureAwait(false) is { } profile) profiles.Add(profile);
        }
        return profiles;
    }
    public async Task<AirlineProfile?> FindAsync(string icao, CancellationToken cancellationToken)
    {
        ValidateKey(icao);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadAsync(icao, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private async Task<AirlineProfile?> ReadAsync(string icao, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, icao + ".json");
        var candidates = new List<string> { path, path + ".bak" };
        if (bundledDirectory is not null) candidates.Add(Path.Combine(bundledDirectory, icao + ".json"));
        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var profile = ProfileJsonCodec.Parse(await File.ReadAllTextAsync(candidate, cancellationToken).ConfigureAwait(false));
                if (profile.Icao != icao) throw new ArgumentException("Profile key mismatch.");
                if (candidate.EndsWith(".bak", StringComparison.Ordinal)) diagnostic?.Invoke($"{icao}: restored last valid profile revision.");
                return profile;
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException or IOException or UnauthorizedAccessException)
            { diagnostic?.Invoke($"{icao}: profile rejected; trying last valid revision, bundled or generic preferences."); }
        }
        return icao == "GENERIC" ? SampleSessionFactory.GenericProfile() : null;
    }
    public async Task SaveAsync(AirlineProfile profile, CancellationToken cancellationToken)
    {
        ProfileJsonCodec.Validate(profile); ValidateKey(profile.Icao);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            using var ownership = new FileStream(Path.Combine(directory, ".profiles.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var current = await ReadAsync(profile.Icao, cancellationToken).ConfigureAwait(false);
            if (current is not null && profile.Revision != current.Revision + 1 || current is null && profile.Revision != 1)
                throw new InvalidOperationException("Profile revision changed. Reload before saving.");
            string path = Path.Combine(directory, profile.Icao + ".json");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, ProfileJsonCodec.Serialize(profile), cancellationToken).ConfigureAwait(false);
                _ = ProfileJsonCodec.Parse(await File.ReadAllTextAsync(temporary, cancellationToken).ConfigureAwait(false));
                // Keep the validated previous revision, rather than backing up possibly corrupted input.
                if (current is not null) await File.WriteAllTextAsync(path + ".bak", ProfileJsonCodec.Serialize(current), cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
    }
    private static void ValidateKey(string value)
    {
        if (value != "GENERIC" && (value.Length != 3 || value.Any(character => character is < 'A' or > 'Z')))
            throw new ArgumentException("Use GENERIC or an uppercase three-letter airline ICAO code.");
    }
}
