using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using ADM.Domain;

namespace ADM.Conversation;

public sealed record PhraseVariant(string Text, int Weight);
public sealed record PersonaDefinition(string Id, string Name, string Tone, string Language,
    ImmutableDictionary<string, ImmutableArray<PhraseVariant>> Templates);
public sealed record PersonaDocument(int SchemaVersion, ImmutableArray<PersonaDefinition> Personas);

public sealed class PersonaCatalogue
{
    public static readonly string[] RequiredKeys = ["welcome", "boarding.approval", "boarding.approval.no-fuel", "closure.approval", "handover.verified", "handover.attested", "handover.simulated", "arrival.complete",
        "services.start.approval", "services.starting", "disruption.gate-documents", "disruption.gate-staff", "disruption.passenger-processing", "disruption.resolved"];
    private readonly ImmutableDictionary<string, PersonaDefinition> _personas;
    private static readonly string[] OptionalKeys = ["services.start.gsx", "services.start.gsx-load", "boarding.approval.gate", "boarding.approval.gate-no-fuel", "handover.signoff"];
    public PersonaCatalogue(IEnumerable<PersonaDefinition> personas)
    {
        var entries = personas.ToArray();
        if (entries.Length is 0 or > 32 || entries.Any(persona => persona is null) || entries.Select(persona => persona.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new ArgumentException("Persona IDs must be unique and the catalogue must not be empty.");
        foreach (var persona in entries)
        {
            if (string.IsNullOrWhiteSpace(persona.Id) || persona.Id.Length > 64 || string.IsNullOrWhiteSpace(persona.Name) || persona.Name.Length > 40 || persona.Name != persona.Name.Trim() || persona.Tone is not ("WarmProfessional" or "FormalConcise" or "FriendlyConcise") || persona.Language is not ("en-GB" or "en-US") || persona.Templates is null)
                throw new ArgumentException("Persona identity, tone or language is invalid.");
            if (persona.Templates.Keys.Any(key => !RequiredKeys.Contains(key, StringComparer.Ordinal) && !OptionalKeys.Contains(key, StringComparer.Ordinal)))
                throw new ArgumentException("Only approved semantic template keys may be configured; operational clarification text cannot be overridden.");
            foreach (string key in RequiredKeys.Concat(OptionalKeys.Where(persona.Templates.ContainsKey)))
            {
                if (!persona.Templates.TryGetValue(key, out var variants) || variants.Length is < 2 or > 16 || variants.Any(variant =>
                    variant is null || variant.Weight is < 1 or > 100 || string.IsNullOrWhiteSpace(variant.Text) || variant.Text.Length > 500 || Regex.IsMatch(variant.Text, "[{}]", RegexOptions.CultureInvariant)))
                    throw new ArgumentException($"Persona {persona.Id} needs at least two valid, bounded variants for {key}.");
                if (key.StartsWith("disruption.", StringComparison.Ordinal) && variants.Any(variant => !variant.Text.Contains("simulated", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Disruption phrases must identify their simulated origin.");
            }
        }
        _personas = entries.ToImmutableDictionary(persona => persona.Id, StringComparer.Ordinal);
    }
    public static PersonaCatalogue FromJson(string json)
    {
        var document = JsonSerializer.Deserialize<PersonaDocument>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 });
        if (document is null || document.SchemaVersion != 1 || document.Personas.IsDefault) throw new ArgumentException("Unsupported persona catalogue schema.");
        return new(document.Personas);
    }
    public static PersonaCatalogue LoadBundled()
    {
        using var stream = typeof(PersonaCatalogue).Assembly.GetManifestResourceStream("ADM.Conversation.Personas.json")
            ?? throw new InvalidOperationException("The bundled dispatcher catalogue is missing.");
        using var reader = new System.IO.StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
    public PersonaDefinition Get(string id) => _personas.TryGetValue(id, out var persona) ? persona : throw new ArgumentException("Unknown persisted persona ID.");
    public DispatcherPersona Select(Guid sessionId, string? previousId = null)
    {
        var eligible = _personas.Values.Where(persona => persona.Id != previousId).ToArray();
        var entries = (eligible.Length > 0 ? eligible : _personas.Values.ToArray()).OrderBy(persona => persona.Id, StringComparer.Ordinal).ToArray();
        uint seed = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(System.Security.Cryptography.SHA256.HashData(sessionId.ToByteArray()));
        var selected = entries[seed % entries.Length];
        return new(selected.Id, selected.Name, selected.Tone, selected.Language);
    }
}
