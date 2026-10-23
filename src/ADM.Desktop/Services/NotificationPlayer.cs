using System.IO;
using System.Windows.Media;

namespace ADM.Desktop.Services;

public interface INotificationPlayer
{
    event EventHandler<string>? PlaybackFailed;
    void Play(double volume);
    void PlayOutgoing(double volume) { }
}
public sealed class NotificationPlayer : INotificationPlayer, IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly MediaPlayer _outgoing = new();
    public NotificationPlayer()
    {
        _player.MediaFailed += (_, args) => PlaybackFailed?.Invoke(this, $"Sound unavailable: {args.ErrorException.Message}");
        _outgoing.MediaFailed += (_, args) => PlaybackFailed?.Invoke(this, $"Sent sound unavailable: {args.ErrorException.Message}");
    }
    public event EventHandler<string>? PlaybackFailed;
    public static string AssetPath(string baseDirectory)
    {
        string supplied = Path.Combine(baseDirectory, "Assets", "incoming.mp3");
        return File.Exists(supplied) ? supplied : Path.Combine(baseDirectory, "Assets", "incoming.wav");
    }
    public void Play(double volume)
    {
        PlayFile(_player, AssetPath(AppContext.BaseDirectory), volume);
    }
    public static string OutgoingAssetPath(string baseDirectory) => Path.Combine(baseDirectory, "Assets", "outgoing.mp3");
    public void PlayOutgoing(double volume) => PlayFile(_outgoing, OutgoingAssetPath(AppContext.BaseDirectory), volume);
    private void PlayFile(MediaPlayer player, string path, double volume)
    {
        if (!File.Exists(path)) { PlaybackFailed?.Invoke(this, "The local notification sound is missing."); return; }
        try
        {
            player.Stop();
            player.Volume = Math.Clamp(volume, 0, 1);
            player.Open(new Uri(path));
            player.Play();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
        { PlaybackFailed?.Invoke(this, $"Sound unavailable: {exception.Message}"); }
    }
    public void Dispose() { _player.Close(); _outgoing.Close(); }
}
