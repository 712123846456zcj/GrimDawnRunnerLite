namespace Wpf_gdRunnerLite.Models;

public sealed record FontPackage(
    string Id,
    string Name,
    string Description,
    string ArchivePath,
    string? PreviewPath);

public sealed record LocalizationContentItem(
    string Path,
    string Description);

public sealed record FontInstallationResult(
    string DestinationDirectory,
    int InstalledFileCount);
