using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
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

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<FontUpdateCheckResult> CheckForUpdatesAsync(
        IReadOnlyDictionary<string, FontPackageState> localStates,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(ManifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        FontPackageManifest manifest = await JsonSerializer.DeserializeAsync<FontPackageManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("远端字体清单为空。");
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException($"暂不支持远端清单版本：{manifest.SchemaVersion}");
        }

        return await EvaluateManifestAsync(
            manifest,
            localStates,
            LocalizationService.BundledFontBundleDirectory,
            cancellationToken);
    }

    public static async Task<FontUpdateCheckResult> EvaluateManifestAsync(
        FontPackageManifest manifest,
        IReadOnlyDictionary<string, FontPackageState> localStates,
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException($"暂不支持远端清单版本：{manifest.SchemaVersion}");
        }

        packageDirectory = Path.GetFullPath(packageDirectory);
        var updates = new List<FontPackageUpdate>();
        var localHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (RemoteFontPackage package in manifest.FontPackages)
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

        return new FontUpdateCheckResult(manifest, updates, localHashes);
    }

    public static async Task<FontPackageDownloadResult> DownloadPackageAsync(
        RemoteFontPackage package,
        IProgress<FontDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package);
        Directory.CreateDirectory(LocalizationService.BundledFontBundleDirectory);

        string archiveFileName = EnsureSimpleFileName(package.ArchiveFileName, "archiveFileName");
        string archivePath = Path.Combine(LocalizationService.BundledFontBundleDirectory, archiveFileName);
        string archiveTemporaryPath = archivePath + ".download";
        string metadataPath = Path.Combine(
            LocalizationService.BundledFontBundleDirectory,
            Path.GetFileNameWithoutExtension(archiveFileName) + ".package.json");
        string metadataTemporaryPath = metadataPath + ".download";

        string? previewPath = null;
        string? previewTemporaryPath = null;
        if (!string.IsNullOrWhiteSpace(package.PreviewUrl) && !string.IsNullOrWhiteSpace(package.PreviewFileName))
        {
            string previewFileName = EnsureSimpleFileName(package.PreviewFileName, "previewFileName");
            previewPath = Path.Combine(LocalizationService.BundledFontBundleDirectory, previewFileName);
            previewTemporaryPath = previewPath + ".download";
        }

        try
        {
            await DownloadFileAsync(
                package.ArchiveUrl,
                archiveTemporaryPath,
                "正在下载字体包",
                0,
                previewTemporaryPath is null ? 100 : 92,
                progress,
                cancellationToken);

            string archiveHash = await ComputeSha256Async(archiveTemporaryPath, cancellationToken);
            string expectedArchiveHash = NormalizeHash(package.Sha256);
            if (expectedArchiveHash.Length != 64 ||
                !string.Equals(archiveHash, expectedArchiveHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("字体 ZIP 的 SHA-256 校验失败。");
            }

            if (previewTemporaryPath is not null)
            {
                await DownloadFileAsync(
                    package.PreviewUrl,
                    previewTemporaryPath,
                    "正在下载预览图",
                    92,
                    8,
                    progress,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(package.PreviewSha256))
                {
                    string previewHash = await ComputeSha256Async(previewTemporaryPath, cancellationToken);
                    if (!string.Equals(previewHash, NormalizeHash(package.PreviewSha256), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("字体预览图的 SHA-256 校验失败。");
                    }
                }
            }

            string metadataJson = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataTemporaryPath, metadataJson, cancellationToken);

            File.Move(archiveTemporaryPath, archivePath, true);
            if (previewTemporaryPath is not null && previewPath is not null)
            {
                File.Move(previewTemporaryPath, previewPath, true);
            }
            File.Move(metadataTemporaryPath, metadataPath, true);

            progress?.Report(new FontDownloadProgress("下载完成", 100, package.Size, package.Size > 0 ? package.Size : null));
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

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        string stage,
        double basePercentage,
        double percentageSpan,
        IProgress<FontDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Uri uri = ValidateHttpsUrl(url);
        using HttpResponseMessage response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        long received = 0;
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream output = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        byte[] buffer = new byte[1024 * 128];
        while (true)
        {
            int count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            received += count;
            double filePercentage = total is > 0 ? Math.Clamp(received * 100d / total.Value, 0, 100) : 0;
            progress?.Report(new FontDownloadProgress(
                stage,
                basePercentage + filePercentage * percentageSpan / 100d,
                received,
                total));
        }

        await output.FlushAsync(cancellationToken);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseProxy = true,
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"GrimDawnRunnerLite/{version}");
        return client;
    }

    private static void ValidatePackage(RemoteFontPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Id)) throw new InvalidDataException("远端字体缺少 id。");
        if (string.IsNullOrWhiteSpace(package.Name)) throw new InvalidDataException($"远端字体 {package.Id} 缺少中文名称。");
        if (string.IsNullOrWhiteSpace(package.EnglishName)) throw new InvalidDataException($"远端字体 {package.Id} 缺少英文名称。");
        if (string.IsNullOrWhiteSpace(package.Version)) throw new InvalidDataException($"远端字体 {package.Id} 缺少版本号。");
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
}
