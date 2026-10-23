using ADM.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ADM.EndToEnd.Tests;

public sealed class DiagnosticLoggingTests
{
    [Fact]
    public void An_unwritable_diagnostics_directory_cannot_break_the_calling_operation()
    {
        string path = Path.GetTempFileName();
        try
        {
            using var provider = new LocalFileLoggerProvider(path);
            var logger = provider.CreateLogger("test");
            logger.LogInformation("Handling continues even when the log path is a file.");
            Assert.Equal("", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Repeated_logging_retains_seven_days_and_caps_the_current_file()
    {
        string directory = Path.Combine(Path.GetTempPath(), "adm-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (int day = 1; day <= 12; day++)
                File.WriteAllText(Path.Combine(directory, $"adm-{DateTime.UtcNow.AddDays(-day):yyyy-MM-dd}.log"), "old");
            using var provider = new LocalFileLoggerProvider(directory);
            var logger = provider.CreateLogger("test"); logger.LogInformation("Started.");
            Assert.Equal(7, Directory.GetFiles(directory, "adm-*.log").Length);
            string current = Path.Combine(directory, $"adm-{DateTime.UtcNow:yyyy-MM-dd}.log");
            using (var file = File.OpenWrite(current)) file.SetLength(4 * 1024 * 1024);
            logger.LogWarning("This cannot grow a capped daily file.");
            Assert.Equal(4 * 1024 * 1024, new FileInfo(current).Length);
        }
        finally { Directory.Delete(directory, true); }
    }
}
