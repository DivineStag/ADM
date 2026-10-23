using Microsoft.Extensions.Logging;

namespace ADM.Infrastructure;

/// <summary>Best-effort daily diagnostics, limited to seven files and 4 MiB per day.</summary>
public sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly string _directory;
    private DateOnly? _retentionDate;
    public LocalFileLoggerProvider(string directory)
    {
        _directory = directory;
    }

    public ILogger CreateLogger(string categoryName) => new LocalLogger(this, categoryName);
    public void Dispose() { }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var now = DateTimeOffset.UtcNow;
                string path = Path.Combine(_directory, $"adm-{now:yyyy-MM-dd}.log");
                var date = DateOnly.FromDateTime(now.UtcDateTime);
                if (_retentionDate != date)
                {
                    foreach (string old in Directory.EnumerateFiles(_directory, "adm-*.log")
                        .Where(file => file != path).OrderDescending().Skip(6))
                    {
                        try { File.Delete(old); }
                        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
                    }
                    _retentionDate = date;
                }
                if (File.Exists(path) && new FileInfo(path).Length >= 4 * 1024 * 1024) return;
                string line = $"{now:O} [{level}] {category}: {message} {exception}{Environment.NewLine}";
                // Bound a single pathological exception as well as repeated diagnostics.
                if (line.Length > 16_384) line = line[..16_384] + Environment.NewLine;
                if ((File.Exists(path) ? new FileInfo(path).Length : 0) + System.Text.Encoding.UTF8.GetByteCount(line) > 4 * 1024 * 1024) return;
                File.AppendAllText(path, line);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            { /* A locked/full diagnostics folder must never stop handling or shutdown. */ }
        }
    }

    private sealed class LocalLogger(LocalFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
