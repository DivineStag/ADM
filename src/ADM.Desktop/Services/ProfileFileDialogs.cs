using Microsoft.Win32;

namespace ADM.Desktop.Services;

public interface IProfileFileDialogs
{
    string? ChooseImport();
    string? ChooseExport(string airlineIcao);
}
public sealed class ProfileFileDialogs : IProfileFileDialogs
{
    public string? ChooseImport()
    {
        var dialog = new OpenFileDialog { Title = Resources.UiText.Get("ImportProfileTitle"), Filter = "ADM profile (*.json)|*.json", CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
    public string? ChooseExport(string airlineIcao)
    {
        var dialog = new SaveFileDialog { Title = Resources.UiText.Get("ExportProfileTitle"), Filter = "ADM profile (*.json)|*.json", FileName = airlineIcao + ".json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
}
