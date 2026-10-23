using System.Reflection;

namespace ADM.Desktop.Services;

public static class ApplicationVersion
{
    public static string Current { get; } = typeof(ApplicationVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
