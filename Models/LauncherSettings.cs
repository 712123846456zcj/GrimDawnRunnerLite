namespace Wpf_gdRunnerLite.Models;

public sealed class LauncherSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string? GameRoot { get; set; }

    public bool CloseAfterLaunch { get; set; } = true;

    public string LastPage { get; set; } = "home";

    public string PreferredLaunchMode { get; set; } = "x64";

    public string? InstalledFontPackageId { get; set; }

    public string? InstalledFontPackageName { get; set; }

    public DateTimeOffset? FontInstalledAtUtc { get; set; }

    public Dictionary<string, FontPackageState> FontPackageStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
