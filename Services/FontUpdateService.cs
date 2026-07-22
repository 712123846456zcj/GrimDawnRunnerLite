using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Wpf_gdRunnerLite.Models;

namespace Wpf_gdRunnerLite.Services;

public static class FontUpdateService
{
    public const string ManifestUrl =
        "https://raw.githubusercontent.com/712123846456zcj/GrimDawnRunnerLite-Packages/main/GitHubPackageTemplate/manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<FontUpdateCheckResult> CheckForUpdatesAsync(
        IReadOnlyDictionary<string, FontPackageState> localFontStates,
        IReadOnlyDictionary<string, FontPackageState> localTextStates,
        LauncherSettings settings,
        CancellationToken cancellationToken = default)
    {
        NetworkContentResult content = await NetworkDownloadService.GetContentAsync(
            ManifestUrl,
            DownloadNetworkOptions.FromSettings(settings),
            ValidateManifestContent,
            cancellationToken);
        FontPackageManifest manifest = JsonSerializer.Deserialize<FontPackageManifest>(content.Content, JsonOptions)
            ?? throw new InvalidDataException("远端汉化清单为空。");

        FontUpdateCheckResult result = await EvaluateManifestAsync(
            manifest,
            localFontStates,
            localTextStates,
            LocalizationService.BundledFontBundleDirectory,
            LocalizationService.BundledTextBundleDirectory,
            cancellationToken);
        return result with { RouteName = content.RouteName };
    }

    public static async Task<FontUpdateCheckResult> EvaluateManifestAsync(
        FontPackageManifest manifest,
        IReadOnlyDictionary<string, FontPackageState> localFontStates,
        IReadOnlyDictionary<string, FontPackageState> localTextStates,
        string fontPackageDirectory,
        string textPackageDirectory,
        CancellationToken cancellationToken = default)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException($"暂不支持远端清单版本：{manifest.SchemaVersion}");
        }

        PackageEvaluation fontEvaluation = await EvaluatePackagesAsync(
            manifest.FontPackages,
            localFontStates,
            fontPackageDirectory,
            cancellationToken);
        PackageEvaluation textEvaluation = await EvaluatePackagesAsync(
            manifest.TextPackages,
            localTextStates,
            textPackageDirectory,
            cancellationToken);

        return new FontUpdateCheckResult(
            manifest,
            fontEvaluation.Updates,
            fontEvaluation.LocalHashes,
            textEvaluation.Updates,
            textEvaluation.LocalHashes);
    }

    private static async Task<PackageEvaluation> EvaluatePackagesAsync(
        IReadOnlyList<RemoteFontPackage> packages,
        IReadOnlyDictionary<string, FontPackageState> localStates,
        string packageDirectory,
        CancellationToken cancellationToken)
    {
        packageDirectory = Path.GetFullPath(packageDirectory);
        var updates = new List<FontPackageUpdate>();
        var localHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (RemoteFontPackage package in packages)
        {
            ValidatePackage(package);
            string localPath = Path.Combine(
                packageDirectory,
                EnsureSimpleFileName(package.ArchiveFileName, "archiveFileName"));
            bool missing = !File.Exists(localPath);
            bool hashChanged = false;
            bool previewChanged = false;

            if (!missing)
            {
                if (!string.IsNullOrWhiteSpace(package.Sha256))
                {
                    string localHash = await ComputeSha256Async(localPath, cancellationToken);
                    localHashes[package.Id] = localHash;
                    hashChanged = !string.Equals(localHash, NormalizeHash(package.Sha256), StringComparison.OrdinalIgnoreCase);
                }
                else if (package.Size > 0)
                {
                    hashChanged = new FileInfo(localPath).Length != package.Size;
                }
            }

            if (!string.IsNullOrWhiteSpace(package.PreviewFileName) && !string.IsNullOrWhiteSpace(package.PreviewSha256))
            {
                string previewPath = Path.Combine(
                    packageDirectory,
                    EnsureSimpleFileName(package.PreviewFileName, "previewFileName"));
                if (!File.Exists(previewPath))
                {
                    previewChanged = true;
                }
                else
                {
                    string previewHash = await ComputeSha256Async(previewPath, cancellationToken);
                    previewChanged = !string.Equals(previewHash, NormalizeHash(package.PreviewSha256), StringComparison.OrdinalIgnoreCase);
                }
            }

            bool versionChanged = localStates.TryGetValue(package.Id, out FontPackageState? state) &&
                !string.Equals(state.Version, package.Version, StringComparison.OrdinalIgnoreCase);
            if (missing || hashChanged || versionChanged || previewChanged)
            {
                updates.Add(new FontPackageUpdate(package, localPath, missing, versionChanged, hashChanged, previewChanged));
            }
        }

        return new PackageEvaluation(updates, localHashes);
    }

    public static async Task<FontPackageDownloadResult> DownloadPackageAsync(
        RemoteFontPackage package,
        LocalizationPackageType packageType,
        LauncherSettings settings,
        IProgress<FontDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package);
        string packageDirectory = packageType == LocalizationPackageType.Text
            ? LocalizationService.BundledTextBundleDirectory
            : LocalizationService.BundledFontBundleDirectory;
        Directory.CreateDirectory(packageDirectory);

        string archiveFileName = EnsureSimpleFileName(package.ArchiveFileName, "archiveFileName");
        string archivePath = Path.Combine(packageDirectory, archiveFileName);
        string archiveTemporaryPath = archivePath + ".download";
        string metadataPath = Path.Combine(
            packageDirectory,
            Path.GetFileNameWithoutExtension(archiveFileName) + ".package.json");
        string metadataTemporaryPath = metadataPath + ".download";

        string? previewPath = null;
        string? previewTemporaryPath = null;
        if (!string.IsNullOrWhiteSpace(package.PreviewUrl) && !string.IsNullOrWhiteSpace(package.PreviewFileName))
        {
            string previewFileName = EnsureSimpleFileName(package.PreviewFileName, "previewFileName");
            previewPath = Path.Combine(packageDirectory, previewFileName);
            previewTemporaryPath = previewPath + ".download";
        }

        string packageLabel = packageType == LocalizationPackageType.Text ? "汉化文本包" : "字体包";
        try
        {
            DownloadNetworkOptions networkOptions = DownloadNetworkOptions.FromSettings(settings);
            NetworkDownloadResult archiveDownload = await NetworkDownloadService.DownloadFileAsync(
                package.ArchiveUrl,
                archiveTemporaryPath,
                $"正在下载{packageLabel}",
                0,
                previewTemporaryPath is null ? 100 : 92,
                package.Sha256,
                networkOptions,
                progress,
                cancellationToken);
            string archiveHash = archiveDownload.Sha256;
            string completedRouteName = archiveDownload.RouteName;

            if (previewTemporaryPath is not null)
            {
                NetworkDownloadResult previewDownload = await NetworkDownloadService.DownloadFileAsync(
                    package.PreviewUrl,
                    previewTemporaryPath,
                    "正在下载预览图",
                    92,
                    8,
                    package.PreviewSha256,
                    networkOptions,
                    progress,
                    cancellationToken);
                completedRouteName = previewDownload.RouteName;
            }

            string metadataJson = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataTemporaryPath, metadataJson, cancellationToken);

            File.Move(archiveTemporaryPath, archivePath, true);
            if (previewTemporaryPath is not null && previewPath is not null)
            {
                File.Move(previewTemporaryPath, previewPath, true);
            }
            File.Move(metadataTemporaryPath, metadataPath, true);

            progress?.Report(new FontDownloadProgress(
                "下载完成",
                100,
                package.Size,
                package.Size > 0 ? package.Size : null,
                completedRouteName));
            return new FontPackageDownloadResult(package.Id, package.Version, archiveHash, archivePath, previewPath);
        }
        finally
        {
            DeleteTemporaryFile(archiveTemporaryPath);
            DeleteTemporaryFile(metadataTemporaryPath);
            if (previewTemporaryPath is not null) DeleteTemporaryFile(previewTemporaryPath);
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ValidateManifestContent(byte[] content)
    {
        try
        {
            FontPackageManifest? manifest = JsonSerializer.Deserialize<FontPackageManifest>(content, JsonOptions);
            if (manifest is null) return "远端汉化清单为空。";
            if (manifest.SchemaVersion != 1) return $"暂不支持远端清单版本：{manifest.SchemaVersion}";
            return null;
        }
        catch (Exception ex)
        {
            return $"远端汉化清单格式无效：{ex.GetBaseException().Message}";
        }
    }

    private static void ValidatePackage(RemoteFontPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Id)) throw new InvalidDataException("远端包缺少 id。");
        if (string.IsNullOrWhiteSpace(package.Name)) throw new InvalidDataException($"远端包 {package.Id} 缺少中文名称。");
        if (string.IsNullOrWhiteSpace(package.EnglishName)) throw new InvalidDataException($"远端包 {package.Id} 缺少英文名称。");
        if (string.IsNullOrWhiteSpace(package.Version)) throw new InvalidDataException($"远端包 {package.Id} 缺少版本号。");
        EnsureSimpleFileName(package.ArchiveFileName, "archiveFileName");
        ValidateHttpsUrl(package.ArchiveUrl);
    }

    private static string EnsureSimpleFileName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException($"远端字段 {fieldName} 不是有效文件名。");
        }
        return value;
    }

    private static Uri ValidateHttpsUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("远端下载地址必须是有效的 HTTPS URL。");
        }
        return uri;
    }

    private static string NormalizeHash(string value) =>
        value.Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 下次下载会覆盖同名临时文件。
        }
    }

    private sealed record PackageEvaluation(
        IReadOnlyList<FontPackageUpdate> Updates,
        IReadOnlyDictionary<string, string> LocalHashes);
}
