using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace ADM.Desktop.Services;

public sealed record AssistantSetup(bool AutoConnect, ImmutableHashSet<string> Aircraft)
{
    public static AssistantSetup Default { get; } = new(true, []);
}
public interface IAssistantSetupStore
{
    AssistantSetup Load();
    void Save(AssistantSetup setup);
}
public sealed class MemoryAssistantSetupStore : IAssistantSetupStore
{
    private AssistantSetup _value = AssistantSetup.Default;
    public AssistantSetup Load() => _value;
    public void Save(AssistantSetup setup) => _value = setup;
}
public sealed class JsonAssistantSetupStore(string path) : IAssistantSetupStore
{
    public AssistantSetup Load()
    {
        try
        {
            if (!File.Exists(path)) return AssistantSetup.Default;
            if (new FileInfo(path).Length > 1_048_576) return AssistantSetup.Default;
            var setup = JsonSerializer.Deserialize<AssistantSetup>(File.ReadAllText(path));
            return setup is { Aircraft: not null } && setup.Aircraft.Count <= 256 && setup.Aircraft.All(key => !string.IsNullOrWhiteSpace(key) && key.Length <= 200) ? setup : AssistantSetup.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return AssistantSetup.Default; }
    }
    public void Save(AssistantSetup setup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(setup)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
