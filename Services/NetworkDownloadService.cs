using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;
using Wpf_gdRunnerLite.Models;

namespace Wpf_gdRunnerLite.Services;

public static class NetworkDownloadService
{
    public const string AcceleratorPrefix = "https://gh-proxy.com/";
    public const string AcceleratedRouteName = "国内加速";
    public const string OfficialRouteName = "GitHub 官方";

    private static readonly HashSet<string> AllowedGitHubHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    };

    public static SystemProxyInfo GetSystemProxyInfo()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            bool enabled = Convert.ToInt32(key?.GetValue("ProxyEnable") ?? 0) == 1;
            string? address = key?.GetValue("ProxyServer") as string;
            return new SystemProxyInfo(enabled, enabled && !string.IsNullOrWhiteSpace(address) ? address.Trim() : null);
        }
        catch
        {
            return new SystemProxyInfo(false, null);
        }
    }

    public static Uri NormalizeCustomProxyAddress(string? value)
    {
        string text = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("请输入代理地址，例如 127.0.0.1:7890。");
        }

        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.Port is <= 0 or > 65535 ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("代理地址格式无效，请输入 127.0.0.1:7890 或 http://127.0.0.1:7890。");
        }

        return uri;
    }

    public static Task<NetworkContentResult> GetContentAsync(
        string officialUrl,
        DownloadNetworkOptions options,
        CancellationToken cancellationToken = default) =>
        GetContentAsync(officialUrl, options, null, cancellationToken);

    public static async Task<NetworkContentResult> GetContentAsync(
        string officialUrl,
        DownloadNetworkOptions options,
        Func<byte[], string?>? contentValidator,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        foreach (RouteAttempt attempt in CreateAttempts(officialUrl, options))
        {
            try
            {
                using HttpClient client = CreateHttpClient(attempt, options);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(attempt.IsAccelerated ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(15));
                using HttpResponseMessage response = await client.GetAsync(attempt.RequestUri, timeout.Token);
                response.EnsureSuccessStatusCode();
                byte[] content = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                string? validationError = contentValidator?.Invoke(content);
                if (!string.IsNullOrWhiteSpace(validationError)) throw new InvalidDataException(validationError);
                return new NetworkContentResult(content, attempt.DisplayName);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"{attempt.DisplayName}：连接超时");
            }
            catch (Exception ex)
            {
                failures.Add($"{attempt.DisplayName}：{GetBaseMessage(ex)}");
            }
        }

        throw new HttpRequestException("没有可用的更新线路。" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    public static async Task<NetworkDownloadResult> DownloadFileAsync(
        string officialUrl,
        string destinationPath,
        string stage,
        double basePercentage,
        double percentageSpan,
        string? expectedSha256,
        DownloadNetworkOptions options,
        IProgress<FontDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        foreach (RouteAttempt attempt in CreateAttempts(officialUrl, options))
        {
            DeleteTemporaryFile(destinationPath);
            try
            {
                progress?.Report(new FontDownloadProgress(
                    $"{stage} · 正在连接{attempt.DisplayName}",
                    basePercentage,
                    0,
                    null,
                    attempt.DisplayName));

                long received = await DownloadSingleAsync(
                    attempt,
                    options,
                    destinationPath,
                    stage,
                    basePercentage,
                    percentageSpan,
                    progress,
                    cancellationToken);

                string hash = await ComputeSha256Async(destinationPath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                    !string.Equals(hash, NormalizeHash(expectedSha256), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SHA-256 校验不一致");
                }

                return new NetworkDownloadResult(attempt.DisplayName, received, hash);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DeleteTemporaryFile(destinationPath);
                throw;
            }
            catch (OperationCanceledException)
            {
                failures.Add($"{attempt.DisplayName}：连接超时");
                DeleteTemporaryFile(destinationPath);
            }
            catch (Exception ex)
            {
                failures.Add($"{attempt.DisplayName}：{GetBaseMessage(ex)}");
                DeleteTemporaryFile(destinationPath);
            }
        }

        throw new HttpRequestException("所有下载线路均失败。" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    public static async Task<NetworkConnectionTestResult> TestOfficialConnectionAsync(
        string officialUrl,
        DownloadNetworkOptions options,
        CancellationToken cancellationToken = default)
    {
        var officialOptions = options with { RouteMode = DownloadRouteMode.OfficialOnly };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            NetworkContentResult result = await GetContentAsync(officialUrl, officialOptions, cancellationToken);
            stopwatch.Stop();
            return new NetworkConnectionTestResult(
                true,
                $"连接成功 · {stopwatch.ElapsedMilliseconds} ms",
                GetOfficialConnectionName(officialOptions),
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new NetworkConnectionTestResult(
                false,
                GetBaseMessage(ex),
                GetOfficialConnectionName(officialOptions),
                stopwatch.ElapsedMilliseconds);
        }
    }

    public static string GetRouteSummary(DownloadNetworkOptions options) => options.RouteMode switch
    {
        DownloadRouteMode.AcceleratedOnly => "仅国内加速",
        DownloadRouteMode.OfficialOnly => GetOfficialConnectionName(options),
        _ => $"自动 · 国内加速优先，失败后切换{GetOfficialConnectionName(options)}"
    };

    public static string GetOfficialConnectionName(DownloadNetworkOptions options) => options.ProxyMode switch
    {
        NetworkProxyMode.Direct => "GitHub 官方直连",
        NetworkProxyMode.Custom => "GitHub 官方 · 自定义代理",
        _ => "GitHub 官方 · 系统网络"
    };

    private static async Task<long> DownloadSingleAsync(
        RouteAttempt attempt,
        DownloadNetworkOptions options,
        string destinationPath,
        string stage,
        double basePercentage,
        double percentageSpan,
        IProgress<FontDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpClient client = CreateHttpClient(attempt, options);
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerTimeout.CancelAfter(attempt.IsAccelerated ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(20));
        using HttpResponseMessage response = await client.GetAsync(
            attempt.RequestUri,
            HttpCompletionOption.ResponseHeadersRead,
            headerTimeout.Token);
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
                total,
                attempt.DisplayName));
        }

        await output.FlushAsync(cancellationToken);
        return received;
    }

    private static IReadOnlyList<RouteAttempt> CreateAttempts(string officialUrl, DownloadNetworkOptions options)
    {
        Uri officialUri = ValidateOfficialUrl(officialUrl);
        var attempts = new List<RouteAttempt>();
        if (options.RouteMode is DownloadRouteMode.Auto or DownloadRouteMode.AcceleratedOnly)
        {
            attempts.Add(new RouteAttempt(
                AcceleratedRouteName,
                new Uri(AcceleratorPrefix + officialUri.AbsoluteUri),
                true));
        }
        if (options.RouteMode is DownloadRouteMode.Auto or DownloadRouteMode.OfficialOnly)
        {
            attempts.Add(new RouteAttempt(GetOfficialConnectionName(options), officialUri, false));
        }
        return attempts;
    }

    private static HttpClient CreateHttpClient(RouteAttempt attempt, DownloadNetworkOptions options)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        if (attempt.IsAccelerated || options.ProxyMode == NetworkProxyMode.Direct)
        {
            handler.UseProxy = false;
        }
        else if (options.ProxyMode == NetworkProxyMode.Custom)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(NormalizeCustomProxyAddress(options.CustomProxyAddress));
        }
        else
        {
            handler.UseProxy = true;
            handler.Proxy = WebRequest.DefaultWebProxy;
            handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
        }

        var client = new HttpClient(handler);
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"GrimDawnRunnerLite/{version}");
        return client;
    }

    private static Uri ValidateOfficialUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !AllowedGitHubHosts.Contains(uri.Host))
        {
            throw new InvalidDataException("下载地址必须是受支持的 GitHub HTTPS 地址。");
        }
        return uri;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeHash(string value) =>
        value.Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string GetBaseMessage(Exception ex) => ex.GetBaseException().Message;

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 下一条线路会再次覆盖临时文件。
        }
    }

    private sealed record RouteAttempt(string DisplayName, Uri RequestUri, bool IsAccelerated);
}
