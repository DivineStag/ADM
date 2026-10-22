using Microsoft.Extensions.Logging;

namespace ADM.Infrastructure;

/// <summary>Daily local diagnostics, limited to seven daily files. No chat or account identifiers are logged.</summary>
public sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly string _directory;
    public LocalFileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        foreach (string path in Directory.EnumerateFiles(directory, "adm-*.log").OrderDescending().Skip(6))
            File.Delete(path);
    }

    public ILogger CreateLogger(string categoryName) => new LocalLogger(this, categoryName);
    public void Dispose() { }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        lock (_sync)
        {
            string path = Path.Combine(_directory, $"adm-{DateTimeOffset.UtcNow:yyyy-MM-dd}.log");
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} [{level}] {category}: {message} {exception}{Environment.NewLine}");
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
