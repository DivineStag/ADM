using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;
namespace ADM.Desktop.Services;

public interface IInstallationRepair
{
    bool RuntimeAvailable { get; }
    string? FindInstaller();
}
public sealed class InstallationRepair : IInstallationRepair
{
    public bool RuntimeAvailable => File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.FlightSimulator.SimConnect.dll")) && File.Exists(Path.Combine(AppContext.BaseDirectory, "SimConnect.dll"));
    public string? FindInstaller()
    {
        string cached = Path.Combine(AppContext.BaseDirectory, "Maintenance", "ADM-Setup.exe");
        string checksum = cached + ".sha256";
        if (File.Exists(cached) && File.Exists(checksum))
        {
            using var stream = File.OpenRead(cached);
            if (Convert.ToHexString(SHA256.HashData(stream)).Equals(File.ReadAllText(checksum).Trim(), StringComparison.OrdinalIgnoreCase)) return cached;
        }
        var dialog = new OpenFileDialog { Title = "Select the ADM setup application you downloaded", Filter = "ADM setup (ADM-Setup.exe)|ADM-Setup.exe", CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
}
