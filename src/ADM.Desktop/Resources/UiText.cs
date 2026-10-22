using System.Globalization;
using System.Resources;
namespace ADM.Desktop.Resources;

/// <summary>Neutral English resources; satellite cultures may be added without changing view bindings.</summary>
public static class UiText
{
    private static readonly ResourceManager Manager = new("ADM.Desktop.Resources.UiStrings", typeof(UiText).Assembly);
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? throw new InvalidOperationException("Missing interface resource: " + key);
    public static string Chat => Get(nameof(Chat));
    public static string Services => Get(nameof(Services));
    public static string Flight => Get(nameof(Flight));
    public static string Settings => Get(nameof(Settings));
    public static string RealismTitle => Get(nameof(RealismTitle));
    public static string AllowDelays => Get(nameof(AllowDelays));
    public static string DelayChance => Get(nameof(DelayChance));
    public static string MaximumDelay => Get(nameof(MaximumDelay));
    public static string RealismNote => Get(nameof(RealismNote));
    public static string ProfileFiles => Get(nameof(ProfileFiles));
    public static string ImportProfile => Get(nameof(ImportProfile));
    public static string ExportProfile => Get(nameof(ExportProfile));
    public static string AirlineRealism => Get(nameof(AirlineRealism));
    public static string AirlineRealismNote => Get(nameof(AirlineRealismNote));
    public static string AirlineDelayChance => Get(nameof(AirlineDelayChance));
    public static string AirlineMaximumDelay => Get(nameof(AirlineMaximumDelay));
    public static string ProfileFilesNote => Get(nameof(ProfileFilesNote));
}
