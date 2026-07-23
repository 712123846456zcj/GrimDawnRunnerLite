namespace Wpf_gdRunnerLite.Models;

public sealed class FontPackageState
{
    public string Version { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class FontPackageManifest
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<RemoteFontPackage> FontPackages { get; set; } = [];

    public List<RemoteFontPackage> TextPackages { get; set; } = [];
}

public sealed class RemoteFontPackage
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string EnglishName { get; set; } = "";

    public string AuthorName { get; set; } = "";

    public string SharerName { get; set; } = "";

    public string Version { get; set; } = "";

    public string Description { get; set; } = "";

    public string SupportedGameVersion { get; set; } = "全版本通用";

    public string ArchiveUrl { get; set; } = "";

    public string PreviewUrl { get; set; } = "";

    public string ArchiveFileName { get; set; } = "";

    public string PreviewFileName { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public string PreviewSha256 { get; set; } = "";

    public long Size { get; set; }
}

public sealed record FontPackageUpdate(
    RemoteFontPackage Package,
    string LocalArchivePath,
    bool IsMissing,
    bool VersionChanged,
    bool HashChanged,
    bool PreviewChanged);

public sealed record FontDownloadProgress(
    string Stage,
    double Percentage,
    long BytesReceived,
    long? TotalBytes,
    string RouteName = "");

public sealed record FontPackageDownloadResult(
    string PackageId,
    string Version,
    string Sha256,
    string ArchivePath,
    string? PreviewPath);

public sealed record FontUpdateCheckResult(
    FontPackageManifest Manifest,
    IReadOnlyList<FontPackageUpdate> Updates,
    IReadOnlyDictionary<string, string> LocalHashes,
    IReadOnlyList<FontPackageUpdate> TextUpdates,
    IReadOnlyDictionary<string, string> TextLocalHashes,
    string RouteName = "");
