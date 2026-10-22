using System.IO;
using ADM.Desktop.Services;

namespace ADM.Desktop.Presentation;

public sealed class UpdatesViewModel : ObservableObject
{
    private readonly GitHubUpdates _updates;
    private readonly Func<bool> _handlingActive;
    private ReleaseUpdate? _available;
    private string _status;
    public UpdatesViewModel(GitHubUpdates updates, Func<bool> handlingActive)
    {
        _updates = updates; _handlingActive = handlingActive;
        _status = updates.Configured ? "Updates are checked when ADM opens." : "This local beta is not linked to a published release yet.";
        CheckCommand = new(CheckAsync, _ => Status = "Updates could not be checked. ADM still works offline.", () => updates.Configured);
        InstallCommand = new(InstallAsync, _ => Status = "The update could not be verified or downloaded. Try again later.", () => CanInstall);
    }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Available => _available is not null;
    public bool CanInstall => Available && !_handlingActive();
    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public event Action<string>? InstallerReady;
    public void Refresh() { Raise(nameof(CanInstall)); InstallCommand.Refresh(); }
    public async Task CheckAsync()
    {
        if (!_updates.Configured) return;
        Status = "Checking for updates…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _available = await _updates.CheckAsync(timeout.Token);
        Status = _available is null ? _updates.HasPublishedRelease ? "You're up to date with the latest public release." : "No public release is available yet. Your local beta still works." : $"ADM {_available.Version} is available. Install after ground handling is finished.";
        Raise(nameof(Available)); Refresh();
    }
    private async Task InstallAsync()
    {
        if (_available is null || _handlingActive()) return;
        Status = "Downloading and checking the installer…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        string installer = await _updates.DownloadAsync(_available, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADM", "updates"), timeout.Token);
        if (_handlingActive()) { Status = "Download ready. Finish ground handling before installing."; return; }
        InstallerReady?.Invoke(installer);
    }
}
