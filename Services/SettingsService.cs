using System.IO;
using System.Text;
using System.Text.Json;
using Wpf_gdRunnerLite.Models;

namespace Wpf_gdRunnerLite.Services;

public static class SettingsService
{
    private static readonly object SyncRoot = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string SettingsDirectory => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static string LegacySettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GrimDawnRunnerLite");

    private static string LegacySettingsPath => Path.Combine(LegacySettingsDirectory, "settings.json");

    public static LauncherSettings Load()
    {
        lock (SyncRoot)
        {
            LauncherSettings? portableSettings = TryRead(SettingsPath);
            if (portableSettings is not null)
            {
                Normalize(portableSettings);
                return portableSettings;
            }

            LauncherSettings? legacySettings = TryRead(LegacySettingsPath);
            if (legacySettings is null) return new LauncherSettings();

            Normalize(legacySettings);
            try
            {
                SaveCore(legacySettings);
                DeleteLegacySettings();
            }
            catch
            {
                // 便携配置写入失败时仍使用已读取的旧配置，本次运行不会丢失设置。
            }

            return legacySettings;
        }
    }

    public static void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (SyncRoot)
        {
            Normalize(settings);
            SaveCore(settings);
        }
    }

    private static LauncherSettings? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<LauncherSettings>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCore(LauncherSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);

        string temporaryPath = SettingsPath + ".tmp";
        string backupPath = SettingsPath + ".bak";
        string json = JsonSerializer.Serialize(settings, JsonOptions);

        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));

            if (File.Exists(SettingsPath))
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Replace(temporaryPath, SettingsPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void DeleteLegacySettings()
    {
        try
        {
            foreach (string path in new[]
                     {
                         LegacySettingsPath,
                         LegacySettingsPath + ".bak",
                         LegacySettingsPath + ".tmp"
                     })
            {
                if (File.Exists(path)) File.Delete(path);
            }

            if (Directory.Exists(LegacySettingsDirectory) &&
                !Directory.EnumerateFileSystemEntries(LegacySettingsDirectory).Any())
            {
                Directory.Delete(LegacySettingsDirectory);
            }
        }
        catch
        {
            // 迁移后的便携配置已经可用，旧目录清理失败不影响运行。
        }
    }

    private static void Normalize(LauncherSettings settings)
    {
        settings.SchemaVersion = Math.Max(1, settings.SchemaVersion);
        settings.FontPackageStates = settings.FontPackageStates is null
            ? new Dictionary<string, FontPackageState>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, FontPackageState>(settings.FontPackageStates, StringComparer.OrdinalIgnoreCase);

        string[] validPages = ["home", "account", "mods", "localization", "usage", "about"];
        if (!validPages.Contains(settings.LastPage, StringComparer.OrdinalIgnoreCase))
        {
            settings.LastPage = "home";
        }

        string[] validModes = ["x64", "x86", "legacy"];
        if (!validModes.Contains(settings.PreferredLaunchMode, StringComparer.OrdinalIgnoreCase))
        {
            settings.PreferredLaunchMode = "x64";
        }
    }
}
