using System.Net.WebSockets;
using System.Text;

namespace ADM.Integrations.Gsx;

public interface IGsxSocket : IAsyncDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);
    Task SendAsync(string text, CancellationToken cancellationToken);
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}

public sealed class GsxSocket : IGsxSocket
{
    private const int MaximumMessageBytes = 4 * 1024 * 1024;
    private readonly ClientWebSocket _socket = new();
    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) => _socket.ConnectAsync(endpoint, cancellationToken);
    public async Task SendAsync(string text, CancellationToken cancellationToken) =>
        await _socket.SendAsync(Encoding.UTF8.GetBytes(text).AsMemory(), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        using var message = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("GSX sent a non-text frame.");
            if (message.Length + result.Count > MaximumMessageBytes) throw new InvalidDataException("GSX frame exceeds the 4 MiB limit.");
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return new UTF8Encoding(false, true).GetString(message.GetBuffer(), 0, checked((int)message.Length));
    }
    public ValueTask DisposeAsync() { _socket.Abort(); _socket.Dispose(); return ValueTask.CompletedTask; }
}

public static class GsxPortDiscovery
{
    public static int Read(string iniPath)
    {
        try
        {
            bool section = false;
            foreach (string original in File.ReadLines(iniPath))
            {
                string line = original.Split(';', '#')[0].Trim();
                if (line.StartsWith('[')) { section = line.Equals("[gsx]", StringComparison.OrdinalIgnoreCase); continue; }
                var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (section && pair.Length == 2 && pair[0].Equals("remote_server_port", StringComparison.OrdinalIgnoreCase))
                    return int.TryParse(pair[1], out int port) && port is > 0 and <= 65535 ? port : 8744;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Documented default on unreadable/missing INI. */ }
        return 8744;
    }
}
