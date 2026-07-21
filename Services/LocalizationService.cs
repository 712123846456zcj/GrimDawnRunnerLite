using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Wpf_gdRunnerLite.Models;

namespace Wpf_gdRunnerLite.Services;

public static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, (string Id, string Description, int Order)> KnownPackages =
        new Dictionary<string, (string Id, string Description, int Order)>(StringComparer.OrdinalIgnoreCase)
        {
            ["仓耳明楷"] = ("canger-mingkai", "清晰端正的明楷风格，适合任务说明、装备词条和长时间阅读。", 0),
            ["汉仪正圆"] = ("hanyi-zhengyuan", "圆润醒目的中文字体，在高分辨率界面中具有较好的辨识度。", 1)
        };

    public static string BundledFontBundleDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "FontBundle");


    public static string UserGameSettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games",
        "Grim Dawn",
        "Settings");

    public static string GetGameSettingsDirectory(string gameRoot) => Path.Combine(gameRoot, "settings");

    public static string GetGameFontDirectory(string gameRoot) => Path.Combine(GetGameSettingsDirectory(gameRoot), "Fonts", "zh");

    public static IReadOnlyList<FontPackage> GetAvailableFontPackages()
    {
        var packages = new Dictionary<string, (FontPackage Package, int Order)>(StringComparer.OrdinalIgnoreCase);
        ScanPackageDirectory(BundledFontBundleDirectory, packages);

        return packages.Values
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Package.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Package)
            .ToArray();
    }

    private static void ScanPackageDirectory(
        string directory,
        IDictionary<string, (FontPackage Package, int Order)> packages)
    {
        if (!Directory.Exists(directory)) return;

        IEnumerable<string> archives;
        try
        {
            archives = Directory.EnumerateFiles(directory, "*.zip", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            return;
        }

        foreach (string archivePath in archives)
        {
            string name = Path.GetFileNameWithoutExtension(archivePath).Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            RemoteFontPackage? remote = TryLoadPackageMetadata(archivePath);
            if (!string.IsNullOrWhiteSpace(remote?.Name)) name = remote.Name.Trim();

            bool known = KnownPackages.TryGetValue(name, out var metadata);
            string id = !string.IsNullOrWhiteSpace(remote?.Id)
                ? remote.Id
                : known ? metadata.Id : "font:" + name;
            string description = !string.IsNullOrWhiteSpace(remote?.Description)
                ? remote.Description
                : known
                    ? metadata.Description
                    : "本地字体包。安装前会检测旧字体和汉化内容，并安装到游戏根目录。";
            int order = known ? metadata.Order : 100;
            string? previewPath = null;
            if (!string.IsNullOrWhiteSpace(remote?.PreviewFileName))
            {
                string candidate = Path.Combine(directory, Path.GetFileName(remote.PreviewFileName));
                if (File.Exists(candidate)) previewPath = candidate;
            }
            previewPath ??= FindPreview(directory, Path.GetFileNameWithoutExtension(archivePath));

            packages[id] = (
                new FontPackage(id, name, description, archivePath, previewPath),
                order);
        }
    }

    private static RemoteFontPackage? TryLoadPackageMetadata(string archivePath)
    {
        try
        {
            string metadataPath = Path.Combine(
                Path.GetDirectoryName(archivePath) ?? "",
                Path.GetFileNameWithoutExtension(archivePath) + ".package.json");
            if (!File.Exists(metadataPath)) return null;
            return JsonSerializer.Deserialize<RemoteFontPackage>(
                File.ReadAllText(metadataPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<LocalizationContentItem> DetectExistingFonts(string gameRoot)
    {
        string gameSettings = GetGameSettingsDirectory(gameRoot);
        string userSettings = UserGameSettingsDirectory;
        return new[]
        {
            new LocalizationContentItem(Path.Combine(gameSettings, "Fonts"), "游戏根目录字体"),
            new LocalizationContentItem(Path.Combine(userSettings, "Fonts"), "本地存档设置字体")
        }
        .Where(item => Directory.Exists(item.Path))
        .DistinctBy(item => Path.GetFullPath(item.Path), StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    public static IReadOnlyList<LocalizationContentItem> DetectExistingLocalization(string gameRoot)
    {
        string gameSettings = GetGameSettingsDirectory(gameRoot);
        string userSettings = UserGameSettingsDirectory;

        var candidates = new[]
        {
            new LocalizationContentItem(Path.Combine(gameSettings, "Fonts"), "游戏根目录字体"),
            new LocalizationContentItem(Path.Combine(gameSettings, "text_ZH"), "游戏根目录汉化文本"),
            new LocalizationContentItem(Path.Combine(userSettings, "Fonts"), "本地存档设置字体"),
            new LocalizationContentItem(Path.Combine(userSettings, "text_ZH"), "本地存档设置汉化文本")
        };

        return candidates
            .Where(item => Directory.Exists(item.Path))
            .DistinctBy(item => Path.GetFullPath(item.Path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool RemoveGameFonts(string gameRoot)
    {
        string fontsDirectory = Path.Combine(GetGameSettingsDirectory(gameRoot), "Fonts");
        if (!Directory.Exists(fontsDirectory)) return false;
        Directory.Delete(fontsDirectory, true);
        return true;
    }

    public static int CountInstalledFontFiles(string gameRoot)
    {
        string directory = GetGameFontDirectory(gameRoot);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.fnt", SearchOption.TopDirectoryOnly).Count()
            : 0;
    }

    public static FontInstallationResult InstallFontPackage(
        FontPackage package,
        string gameRoot,
        bool cleanExistingFonts)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!GameLocationService.IsValidGameRoot(gameRoot))
        {
            throw new DirectoryNotFoundException("当前游戏根目录无效，请重新选择 Grim Dawn 主程序。");
        }
        if (!File.Exists(package.ArchivePath))
        {
            throw new FileNotFoundException("字体压缩包不存在。", package.ArchivePath);
        }

        string gameSettings = GetGameSettingsDirectory(gameRoot);
        string stagingRoot = Path.Combine(gameSettings, $".gdrl-font-stage-{Guid.NewGuid():N}");
        string stagingFontDirectory = Path.Combine(stagingRoot, "Fonts", "zh");
        string destination = GetGameFontDirectory(gameRoot);

        try
        {
            Directory.CreateDirectory(stagingFontDirectory);
            int installedCount = ExtractFontFiles(package.ArchivePath, stagingFontDirectory);
            if (installedCount == 0)
            {
                throw new InvalidDataException("压缩包中没有找到 .fnt 字体文件。");
            }

            if (cleanExistingFonts)
            {
                foreach (LocalizationContentItem item in DetectExistingFonts(gameRoot))
                {
                    Directory.Delete(item.Path, true);
                }
            }
            else if (Directory.Exists(Path.Combine(gameSettings, "Fonts")))
            {
                Directory.Delete(Path.Combine(gameSettings, "Fonts"), true);
            }

            string destinationParent = Directory.GetParent(destination)?.FullName
                ?? throw new InvalidOperationException("字体安装目录无效。");
            Directory.CreateDirectory(destinationParent);
            Directory.Move(stagingFontDirectory, destination);

            return new FontInstallationResult(destination, installedCount);
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
        }
    }

    private static int ExtractFontFiles(string archivePath, string destinationDirectory)
    {
        int installedCount = 0;
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase)) continue;

            string fileName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrWhiteSpace(fileName)) continue;
            if (!fileNames.Add(fileName))
            {
                throw new InvalidDataException($"字体包中包含重复文件：{fileName}");
            }

            string destinationPath = Path.Combine(destinationDirectory, fileName);
            using Stream input = entry.Open();
            using FileStream output = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            installedCount++;
        }

        return installedCount;
    }

    private static string? FindPreview(string bundleDirectory, string baseName)
    {
        string[] names =
        [
            $"{baseName}.preview.png",
            $"{baseName}.preview.jpg",
            $"{baseName}.preview.jpeg",
            $"{baseName}.png",
            $"{baseName}.jpg",
            $"{baseName}.jpeg"
        ];

        return names
            .Select(name => Path.Combine(bundleDirectory, name))
            .FirstOrDefault(File.Exists);
    }
}
