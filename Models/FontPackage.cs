namespace Wpf_gdRunnerLite.Models;

public enum LocalizationPackageType
{
    Font,
    Text
}

public sealed record FontPackage(
    string Id,
    string Name,
    string Description,
    string ArchivePath,
    string? PreviewPath,
    LocalizationPackageType PackageType = LocalizationPackageType.Font,
    string SupportedGameVersion = "全版本通用",
    string Version = "",
    string EnglishName = "");

public sealed record LocalizationContentItem(
    string Path,
    string Description);

public sealed record FontInstallationResult(
    string DestinationDirectory,
    int InstalledFileCount);
