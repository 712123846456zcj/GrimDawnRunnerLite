namespace Wpf_gdRunnerLite.Models;

public enum DownloadRouteMode
{
    Auto,
    AcceleratedOnly,
    OfficialOnly
}

public enum NetworkProxyMode
{
    System,
    Direct,
    Custom
}

public sealed record DownloadNetworkOptions(
    DownloadRouteMode RouteMode,
    NetworkProxyMode ProxyMode,
    string? CustomProxyAddress)
{
    public static DownloadNetworkOptions FromSettings(LauncherSettings settings) =>
        new(settings.DownloadRouteMode, settings.NetworkProxyMode, settings.CustomProxyAddress);
}

public sealed record SystemProxyInfo(bool IsEnabled, string? Address);

public sealed record NetworkContentResult(byte[] Content, string RouteName);

public sealed record NetworkDownloadResult(string RouteName, long BytesReceived, string Sha256);

public sealed record NetworkConnectionTestResult(
    bool Success,
    string Message,
    string RouteName,
    long ElapsedMilliseconds);
