using System.IO;
using System.Windows.Media;

namespace ADM.Desktop.Services;

public interface INotificationPlayer
{
    event EventHandler<string>? PlaybackFailed;
    void Play(double volume);
}
public sealed class NotificationPlayer : INotificationPlayer, IDisposable
{
    private readonly MediaPlayer _player = new();
    public NotificationPlayer() => _player.MediaFailed += (_, args) => PlaybackFailed?.Invoke(this, $"Sound unavailable: {args.ErrorException.Message}");
    public event EventHandler<string>? PlaybackFailed;
    public static string AssetPath(string baseDirectory)
    {
        string supplied = Path.Combine(baseDirectory, "Assets", "incoming.mp3");
        return File.Exists(supplied) ? supplied : Path.Combine(baseDirectory, "Assets", "incoming.wav");
    }
    public void Play(double volume)
    {
        string path = AssetPath(AppContext.BaseDirectory);
        if (!File.Exists(path)) { PlaybackFailed?.Invoke(this, "The local notification sound is missing."); return; }
        try
        {
            _player.Stop();
            _player.Volume = Math.Clamp(volume, 0, 1);
            _player.Open(new Uri(path));
            _player.Play();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
        { PlaybackFailed?.Invoke(this, $"Sound unavailable: {exception.Message}"); }
    }
    public void Dispose() => _player.Close();
}
