using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADM.Domain;
using ADM.Integrations.Gsx;
using Microsoft.Win32;

namespace ADM.Desktop.Services;

// Explicit allowlist. Never add raw logs, exceptions, paths, chat, plan identity or provider payloads.
public sealed record SupportReport(string AppVersion, DateTimeOffset CreatedAt, string SimulatorConnection,
    string GsxConnection, string Stage, bool MenuOpen, bool PromptOpen, string SuggestedAction,
    IReadOnlyDictionary<ServiceKind, ServiceState> Services);
public static class SupportReportJson
{
    public static string Serialize(SupportReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
}
public interface ISupportReportExporter { Task<bool> ExportAsync(SupportReport report, CancellationToken cancellationToken); }
public sealed class SupportReportExporter : ISupportReportExporter
{
    public async Task<bool> ExportAsync(SupportReport report, CancellationToken cancellationToken)
    {
        var dialog = new SaveFileDialog { Title = "Save ADM support report", Filter = "Support report (*.json)|*.json", FileName = "ADM-support.json", AddExtension = true, DefaultExt = ".json" };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return false;
        await File.WriteAllTextAsync(dialog.FileName, SupportReportJson.Serialize(report), cancellationToken);
        return true;
    }
}
public interface IGsxHelpLauncher { void Open(); }
public sealed class GsxHelpLauncher : IGsxHelpLauncher
{
    public void Open()
    {
        int port = GsxPortDiscovery.Read(new GsxRemoteOptions().IniPath);
        Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}/") { UseShellExecute = true });
    }
}
