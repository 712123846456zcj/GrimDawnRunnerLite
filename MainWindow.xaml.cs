using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf_gdRunnerLite.Models;
using Wpf_gdRunnerLite.Services;

namespace Wpf_gdRunnerLite;

public partial class MainWindow : Window
{
    private static readonly Brush AvailableBrush = new SolidColorBrush(Color.FromRgb(79, 199, 120));
    private static readonly Brush MissingBrush = new SolidColorBrush(Color.FromRgb(181, 91, 76));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(215, 168, 75));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(111, 120, 130));
    private static readonly Brush NavSelectedBrush = new SolidColorBrush(Color.FromArgb(48, 215, 168, 75));
    private static readonly Brush NavNormalBrush = Brushes.Transparent;
    private static readonly Regex SteamIdRegex = new(@"^\d{17}$", RegexOptions.Compiled);
    private const string OpenSourceUrl = "https://github.com/712123846456zcj/GrimDawnRunnerLite";
    private const string UpdateUrl = "https://github.com/712123846456zcj/GrimDawnRunnerLite/releases";

    private string _gameRoot;
    private readonly string _forwardedArguments;
    private readonly LauncherSettings _settings;
    private Dictionary<string, FontPackage> _fontPackages = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FontPackage> _textPackages = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLocalizationBusy;
    private bool _fontUpdateCheckInProgress;
    private IReadOnlyList<FontPackageUpdate> _availableFontUpdates = [];
    private IReadOnlyList<FontPackageUpdate> _availableTextUpdates = [];
    private string _currentPage = "home";
    private bool _loadingNetworkSettings;
    private bool _networkTestInProgress;

    private sealed class FontPackageCardViewModel
    {
        public required FontPackage Package { get; init; }
        public required string StatusText { get; init; }
        public required Brush StatusBrush { get; init; }
        public required string ActionText { get; init; }
        public required string PreviewHint { get; init; }
        public BitmapSource? PreviewImage { get; init; }
        public bool CanInstall { get; init; }
        public Visibility DeleteVisibility { get; init; }

        public string Id => Package.Id;
        public string Name => Package.Name;
        public string Description => Package.Description;
        public string SupportedGameVersion => Package.SupportedGameVersion;
    }

    private string X64Executable => Path.Combine(_gameRoot, "x64", "Grim Dawn.exe");
    private string X86Executable => Path.Combine(_gameRoot, "Grim Dawn.exe");
    private string LegacyBatch => Path.Combine(_gameRoot, "Grim Dawn (x86) - Legacy (DX9).bat");
    private string RootSettingsDirectory => Path.Combine(_gameRoot, "steam_settings");
    private string X64SettingsDirectory => Path.Combine(_gameRoot, "x64", "steam_settings");
    private string RootConfigPath => Path.Combine(RootSettingsDirectory, "configs.user.ini");
    private string X64ConfigPath => Path.Combine(X64SettingsDirectory, "configs.user.ini");
    private string SaveDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Grim Dawn");

    public MainWindow(string gameRoot, LauncherSettings settings)
    {
        InitializeComponent();
        _gameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        _forwardedArguments = BuildForwardedArguments(Environment.GetCommandLineArgs().Skip(1));
        _settings = settings;
        CloseAfterLaunchCheckBox.IsChecked = _settings.CloseAfterLaunch;
        ApplyNetworkSettingsToControls();

        ArchitectureText.Text = Environment.Is64BitOperatingSystem ? "64 位 Windows" : "32 位 Windows";
        RecommendationText.Text = Environment.Is64BitOperatingSystem
            ? "检测到 64 位系统，推荐使用 x64；DX9 兼容模式也可用于缓解部分新系统上的长时间运行闪退。"
            : "检测到 32 位系统，请使用 x86 或 DX9 兼容模式。";

        Closing += MainWindow_Closing;
        Loaded += async (_, _) =>
        {
            ApplyGameRoot();
            SelectPage(_settings.LastPage);
            await CheckFontUpdatesAsync(false);
        };
    }

    private void ApplyGameRoot()
    {
        CurrentRootText.Text = _gameRoot;
        AboutRootText.Text = _gameRoot;
        UsageRootText.Text = _gameRoot;
        RefreshAvailability();
        LoadAccountConfiguration();
        RefreshLocalizationPage();
    }

    private void RefreshAvailability()
    {
        bool hasX64 = Environment.Is64BitOperatingSystem && File.Exists(X64Executable);
        bool hasX86 = File.Exists(X86Executable);
        bool hasLegacy = File.Exists(LegacyBatch) || hasX86;

        LaunchX64Button.IsEnabled = hasX64;
        LaunchX86Button.IsEnabled = hasX86;
        LaunchLegacyButton.IsEnabled = hasLegacy;
        X64StatusDot.Fill = hasX64 ? AvailableBrush : MissingBrush;
        X86StatusDot.Fill = hasX86 ? AvailableBrush : MissingBrush;
        LegacyStatusDot.Fill = hasLegacy ? AvailableBrush : MissingBrush;

        LegacyLaunchHint.Text = File.Exists(LegacyBatch)
            ? "使用根目录 Legacy (DX9) 批处理启动"
            : "使用 Grim Dawn.exe + /d3d9 参数启动";

        bool ready = hasX64 || hasX86;
        RootStatusDot.Fill = ready ? AvailableBrush : MissingBrush;
        RootStatusText.Text = ready ? "游戏目录已就绪" : "未在当前目录发现游戏";
        SetOperationStatus(
            ready ? "文件检查完成，可以启动游戏" : "请将发布后的启动器放到 Grim Dawn 游戏根目录",
            ready ? AvailableBrush : MissingBrush);
        RefreshIniStatus();
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode }) return;

        string target;
        string arguments = _forwardedArguments;
        string displayName;

        switch (mode)
        {
            case "x64":
                target = X64Executable;
                displayName = "Grim Dawn x64";
                break;
            case "x86":
                target = X86Executable;
                displayName = "Grim Dawn x86";
                break;
            case "legacy":
                if (File.Exists(LegacyBatch))
                {
                    target = LegacyBatch;
                }
                else
                {
                    target = X86Executable;
                    arguments = CombineArguments("/d3d9", _forwardedArguments);
                }
                displayName = "Grim Dawn x86 / DirectX 9";
                break;
            default:
                return;
        }

        if (!File.Exists(target))
        {
            SetOperationStatus($"启动文件不存在：{target}", MissingBrush);
            RefreshAvailability();
            return;
        }

        try
        {
            _settings.PreferredLaunchMode = mode;
            SaveSettings();

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                WorkingDirectory = _gameRoot,
                UseShellExecute = true,
                Arguments = arguments
            });
            SetOperationStatus($"已启动 {displayName}", AvailableBrush);
            if (CloseAfterLaunchCheckBox.IsChecked == true) Close();
        }
        catch (Exception ex)
        {
            SetOperationStatus($"启动失败：{ex.Message}", MissingBrush);
            MessageBox.Show($"启动 {displayName} 时发生错误：\n\n{ex.Message}", "Grim Dawn Runner Lite", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) SelectPage(page);
    }

    private void SelectPage(string page)
    {
        _currentPage = page;
        HomePage.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        AccountPage.Visibility = page == "account" ? Visibility.Visible : Visibility.Collapsed;
        ModsPage.Visibility = page == "mods" ? Visibility.Visible : Visibility.Collapsed;
        LocalizationPage.Visibility = page == "localization" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        UsagePage.Visibility = page == "usage" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;

        SetNavState(NavHomeButton, page == "home");
        SetNavState(NavAccountButton, page == "account");
        SetNavState(NavModsButton, page == "mods");
        SetNavState(NavLocalizationButton, page == "localization");
        SetNavState(NavSettingsButton, page == "settings");
        SetNavState(NavUsageButton, page == "usage");
        SetNavState(NavAboutButton, page == "about");

        (PageTitleText.Text, PageSubtitleText.Text) = page switch
        {
            "account" => ("离线和联机", "管理局域网或远程联机使用的唯一账户标识 (此自定义账户仅适用于学习版)"),
            "mods" => ("Mod 管理", "集中发现、启用和维护自定义模组"),
            "localization" => ("汉化管理", "一键安装,备份,还原汉化文件"),
            "settings" => ("设置", "下载线路、系统代理与自定义网络设置"),
            "usage" => ("使用说明", "游戏目录、启动模式与联机配置说明"),
            "about" => ("关于", "Grim Dawn Runner Lite 项目信息"),
            _ => ("主页", "选择启动方式并快速访问常用目录")
        };

        if (page == "account") LoadAccountConfiguration();
        if (page == "localization") RefreshLocalizationPage();
        if (page == "settings") RefreshNetworkSettingsSummary();
    }

    private static void SetNavState(Button button, bool selected)
    {
        button.Background = selected ? NavSelectedBrush : NavNormalBrush;
        button.Foreground = selected ? new SolidColorBrush(Color.FromRgb(238, 199, 113)) : new SolidColorBrush(Color.FromRgb(173, 180, 190));
        button.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(215, 168, 75)) : Brushes.Transparent;
    }

    private void LoadAccountConfiguration()
    {
        RefreshIniStatus();
        var candidates = new[] { X64ConfigPath, RootConfigPath }.Where(File.Exists).ToArray();
        if (candidates.Length == 0)
        {
            AccountNameTextBox.Text = string.Empty;
            SteamIdTextBox.Text = string.Empty;
            SetAccountMessage("尚未找到 configs.user.ini；目录存在时保存操作会创建配置文件。", WarningBrush);
            return;
        }

        try
        {
            string? accountName = ReadIniValue(candidates[0], "account_name");
            string? steamId = ReadIniValue(candidates[0], "account_steamid");
            AccountNameTextBox.Text = accountName ?? string.Empty;
            SteamIdTextBox.Text = steamId ?? string.Empty;

            bool mismatch = candidates.Skip(1).Any(path =>
                !string.Equals(ReadIniValue(path, "account_name"), accountName, StringComparison.Ordinal) ||
                !string.Equals(ReadIniValue(path, "account_steamid"), steamId, StringComparison.Ordinal));

            SetAccountMessage(
                mismatch ? "检测到两份配置内容不同；点击保存会将它们同步为当前值。" : "配置读取完成。保存时会同步更新 x86 与 x64 两份配置。",
                mismatch ? WarningBrush : AvailableBrush);
        }
        catch (Exception ex)
        {
            SetAccountMessage($"读取配置失败：{ex.Message}", MissingBrush);
        }
    }

    private void SaveAccountButton_Click(object sender, RoutedEventArgs e)
    {
        string accountName = AccountNameTextBox.Text.Trim();
        string steamId = SteamIdTextBox.Text.Trim();

        if (accountName.Length is < 1 or > 64 || accountName.Any(c => c < 32 || c > 126 || c is '=' or '\r' or '\n'))
        {
            SetAccountMessage("账户名称需为 1–64 个 ASCII 字符，且不包含等号或换行。", MissingBrush);
            AccountNameTextBox.Focus();
            return;
        }
        if (!SteamIdRegex.IsMatch(steamId))
        {
            SetAccountMessage("SteamID 标识需要填写 17 位数字。", MissingBrush);
            SteamIdTextBox.Focus();
            return;
        }

        var targetDirectories = new[] { RootSettingsDirectory, X64SettingsDirectory }
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (targetDirectories.Length == 0)
        {
            SetAccountMessage("未发现 steam_settings 目录，请确认启动器位于游戏根目录。", MissingBrush);
            return;
        }

        try
        {
            int saved = 0;
            foreach (string directory in targetDirectories)
            {
                string configPath = Path.Combine(directory, "configs.user.ini");
                UpdateIniFile(configPath, accountName, steamId);
                saved++;
            }
            RefreshIniStatus();
            SetAccountMessage($"保存完成：已同步更新 {saved} 份配置，并保留 .bak 备份。", AvailableBrush);
            SetOperationStatus("联机账户配置已保存", AvailableBrush);
        }
        catch (Exception ex)
        {
            SetAccountMessage($"保存配置失败：{ex.Message}", MissingBrush);
        }
    }

    private void RefreshIniButton_Click(object sender, RoutedEventArgs e) => LoadAccountConfiguration();
    private void OpenRootSettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsDirectory(RootSettingsDirectory);
    private void OpenX64SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsDirectory(X64SettingsDirectory);

    private void OpenSettingsDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            SetAccountMessage($"目录不存在：{directory}", MissingBrush);
            return;
        }
        OpenDirectory(directory);
    }

    private void RefreshIniStatus()
    {
        UpdateIniStatus(RootConfigPath, RootSettingsDirectory, RootIniStatusDot, RootIniStatusText, RootIniPathText);
        UpdateIniStatus(X64ConfigPath, X64SettingsDirectory, X64IniStatusDot, X64IniStatusText, X64IniPathText);
    }

    private static void UpdateIniStatus(string file, string directory, System.Windows.Shapes.Ellipse dot, TextBlock status, TextBlock pathText)
    {
        pathText.Text = file;
        if (File.Exists(file))
        {
            dot.Fill = AvailableBrush;
            status.Text = "已找到配置";
        }
        else if (Directory.Exists(directory))
        {
            dot.Fill = WarningBrush;
            status.Text = "目录存在 · 保存时创建";
        }
        else
        {
            dot.Fill = MissingBrush;
            status.Text = "目录缺失";
        }
    }

    private static string? ReadIniValue(string path, string key)
    {
        string text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Match match = Regex.Match(text, $@"(?im)^\s*{Regex.Escape(key)}\s*=\s*(.*?)\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void UpdateIniFile(string path, string accountName, string steamId)
    {
        byte[] existingBytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
        string text = Encoding.Latin1.GetString(existingBytes);
        string lineEnding = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        text = SetIniValue(text, "account_name", accountName, lineEnding);
        text = SetIniValue(text, "account_steamid", steamId, lineEnding);

        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(text));
    }

    private static string SetIniValue(string text, string key, string value, string lineEnding)
    {
        var regex = new Regex($@"(?im)^(\s*{Regex.Escape(key)}\s*=\s*)[^\r\n]*");
        if (regex.IsMatch(text)) return regex.Replace(text, match => match.Groups[1].Value + value, 1);
        if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal)) text += lineEnding;
        return text + key + "=" + value + lineEnding;
    }

    private void SetAccountMessage(string message, Brush brush)
    {
        AccountMessageText.Text = message;
        AccountMessageDot.Fill = brush;
    }

    private void ChangeGamePathButton_Click(object sender, RoutedEventArgs e)
    {
        string? gameRoot = GameLocationService.SelectGameRoot(this);
        if (gameRoot is null)
        {
            SetOperationStatus("游戏目录未更改", NeutralBrush);
            return;
        }

        _gameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        _settings.GameRoot = _gameRoot;
        SaveSettings();
        ApplyGameRoot();
        SelectPage("usage");
        SetOperationStatus("已保存并切换到新的游戏根目录", AvailableBrush);
    }

    private async Task CheckFontUpdatesAsync(bool showErrors)
    {
        if (_fontUpdateCheckInProgress) return;
        _fontUpdateCheckInProgress = true;
        CheckFontUpdatesButton.IsEnabled = false;
        CheckTextUpdatesButton.IsEnabled = false;
        SetCheckingButtonState(CheckFontUpdatesButton, FontUpdateButtonText);
        SetCheckingButtonState(CheckTextUpdatesButton, TextUpdateButtonText);

        try
        {
            FontUpdateCheckResult result = await FontUpdateService.CheckForUpdatesAsync(
                _settings.FontPackageStates,
                _settings.TextPackageStates,
                _settings);
            LocalizationNetworkText.Text = $"最近更新线路：{result.RouteName}";
            _availableFontUpdates = result.Updates;
            _availableTextUpdates = result.TextUpdates;

            UpdatePackageStates(
                result.Manifest.FontPackages,
                result.Updates,
                result.LocalHashes,
                _settings.FontPackageStates);
            UpdatePackageStates(
                result.Manifest.TextPackages,
                result.TextUpdates,
                result.TextLocalHashes,
                _settings.TextPackageStates);
            SaveSettings();

            SetUpdateButtonState(
                CheckFontUpdatesButton,
                FontUpdateButtonText,
                _availableFontUpdates.Count,
                "字体");
            SetUpdateButtonState(
                CheckTextUpdatesButton,
                TextUpdateButtonText,
                _availableTextUpdates.Count,
                "文本");
        }
        catch (Exception ex)
        {
            _availableFontUpdates = [];
            _availableTextUpdates = [];
            ResetUpdateButton(CheckFontUpdatesButton, FontUpdateButtonText, "检查更新", ex.Message);
            ResetUpdateButton(CheckTextUpdatesButton, TextUpdateButtonText, "检查更新", ex.Message);
            LocalizationNetworkText.Text = "最近更新线路：连接失败，可在设置中测试网络";
            if (showErrors)
            {
                MessageBox.Show(this, $"获取汉化更新信息失败：\n\n{ex.Message}", "检查汉化更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _fontUpdateCheckInProgress = false;
            CheckFontUpdatesButton.IsEnabled = !_isLocalizationBusy;
            CheckTextUpdatesButton.IsEnabled = !_isLocalizationBusy;
        }
    }

    private static void UpdatePackageStates(
        IReadOnlyList<RemoteFontPackage> packages,
        IReadOnlyList<FontPackageUpdate> updates,
        IReadOnlyDictionary<string, string> localHashes,
        IDictionary<string, FontPackageState> states)
    {
        var updateIds = updates.Select(update => update.Package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (RemoteFontPackage package in packages.Where(package => !updateIds.Contains(package.Id)))
        {
            string hash = localHashes.TryGetValue(package.Id, out string? localHash)
                ? localHash
                : package.Sha256.Trim().ToLowerInvariant();
            states[package.Id] = new FontPackageState
            {
                Version = package.Version,
                Sha256 = hash,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private static void SetCheckingButtonState(Button button, TextBlock text)
    {
        text.Text = "检查中…";
        button.Background = new SolidColorBrush(Color.FromRgb(28, 33, 40));
        button.BorderBrush = new SolidColorBrush(Color.FromRgb(58, 66, 76));
        button.Foreground = new SolidColorBrush(Color.FromRgb(190, 197, 205));
    }

    private static void SetUpdateButtonState(Button button, TextBlock text, int updateCount, string packageLabel)
    {
        if (updateCount > 0)
        {
            text.Text = $"可更新 {updateCount}";
            button.Background = new SolidColorBrush(Color.FromRgb(45, 126, 75));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 184, 111));
            button.Foreground = Brushes.White;
            button.ToolTip = $"发现新的{packageLabel}包或本地文件与远端版本不一致";
        }
        else
        {
            ResetUpdateButton(button, text, "已是最新", $"点击重新检查{packageLabel}更新");
        }
    }

    private static void ResetUpdateButton(Button button, TextBlock text, string label, string toolTip)
    {
        text.Text = label;
        button.Background = new SolidColorBrush(Color.FromRgb(24, 29, 35));
        button.BorderBrush = new SolidColorBrush(Color.FromRgb(53, 61, 71));
        button.Foreground = new SolidColorBrush(Color.FromRgb(157, 166, 176));
        button.ToolTip = toolTip;
    }

    private async void CheckFontUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableFontUpdates.Count == 0) await CheckFontUpdatesAsync(true);
        if (_availableFontUpdates.Count == 0) return;

        var dialog = new FontUpdateDialog(_availableFontUpdates, _settings, LocalizationPackageType.Font)
        {
            Owner = this
        };
        dialog.ShowDialog();
        RefreshLocalizationPage();
        await CheckFontUpdatesAsync(false);
    }

    private async void CheckTextUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableTextUpdates.Count == 0) await CheckFontUpdatesAsync(true);
        if (_availableTextUpdates.Count == 0) return;

        var dialog = new FontUpdateDialog(_availableTextUpdates, _settings, LocalizationPackageType.Text)
        {
            Owner = this
        };
        dialog.ShowDialog();
        RefreshLocalizationPage();
        await CheckFontUpdatesAsync(false);
    }

    private void RefreshLocalizationPage()
    {
        LocalizationGamePathText.Text = LocalizationService.GetGameFontDirectory(_gameRoot);
        LocalizationUserPathText.Text = LocalizationService.UserGameSettingsDirectory;

        IReadOnlyList<FontPackage> packages = LocalizationService.GetAvailableFontPackages();
        _fontPackages = packages.ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);
        FontPackagesItemsControl.ItemsSource = packages.Select(CreateFontPackageCard).ToArray();
        FontPackageCountText.Text = $"已发现 {packages.Count} 款";
        EmptyFontPackagesPanel.Visibility = packages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        IReadOnlyList<FontPackage> textPackages = LocalizationService.GetAvailableTextPackages();
        _textPackages = textPackages.ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);
        TextPackagesItemsControl.ItemsSource = textPackages.Select(CreateTextPackageCard).ToArray();
        TextPackageCountText.Text = $"已发现 {textPackages.Count} 款";
        EmptyTextPackagesPanel.Visibility = textPackages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        int installedFontCount = LocalizationService.CountInstalledFontFiles(_gameRoot);
        int installedTextCount = LocalizationService.CountInstalledTextFiles(_gameRoot);
        IReadOnlyList<LocalizationContentItem> existing = LocalizationService.DetectExistingLocalization(_gameRoot);
        if (installedFontCount > 0 && installedTextCount > 0)
        {
            string packageName = _settings.InstalledFontPackageName ?? "已有字体";
            LocalizationStatusDot.Fill = AvailableBrush;
            LocalizationStatusText.Text = $"当前已启用：{packageName}，字体 {installedFontCount} 个文件，汉化文本 {installedTextCount} 个文件。";
        }
        else if (installedFontCount > 0)
        {
            string packageName = _settings.InstalledFontPackageName ?? "已有字体";
            LocalizationStatusDot.Fill = AvailableBrush;
            LocalizationStatusText.Text = $"当前已启用：{packageName}，游戏根目录中有 {installedFontCount} 个 FNT 文件。";
        }
        else if (installedTextCount > 0)
        {
            LocalizationStatusDot.Fill = AvailableBrush;
            string textPackageName = _settings.InstalledTextPackageName ?? "已有汉化文本";
            LocalizationStatusText.Text = $"当前已启用：{textPackageName}，游戏根目录中有 {installedTextCount} 个文本文件。";
        }
        else if (existing.Count > 0)
        {
            LocalizationStatusDot.Fill = WarningBrush;
            LocalizationStatusText.Text = $"检测到 {existing.Count} 处旧汉化内容；安装字体或文本时会先请求清理确认。";
        }
        else
        {
            LocalizationStatusDot.Fill = NeutralBrush;
            LocalizationStatusText.Text = packages.Count == 0 && textPackages.Count == 0
                ? "没有扫描到本地汉化包，请把 ZIP 和同名预览图放入对应目录。"
                : "尚未在游戏根目录启用汉化字体或汉化文本。";
        }
    }

    private FontPackageCardViewModel CreateFontPackageCard(FontPackage package)
    {
        bool archiveExists = File.Exists(package.ArchivePath);
        bool installed = string.Equals(_settings.InstalledFontPackageId, package.Id, StringComparison.OrdinalIgnoreCase)
            && LocalizationService.CountInstalledFontFiles(_gameRoot) > 0;

        string statusText;
        Brush statusBrush;
        string actionText;
        if (installed)
        {
            statusText = "当前已启用";
            statusBrush = AvailableBrush;
            actionText = "重新安装";
        }
        else if (archiveExists)
        {
            double size = new FileInfo(package.ArchivePath).Length / 1024d / 1024d;
            statusText = $"本地字体包 · {size:0.0} MB";
            statusBrush = new SolidColorBrush(Color.FromRgb(142, 151, 162));
            actionText = "启用字体";
        }
        else
        {
            statusText = $"缺少发行文件：{Path.GetFileName(package.ArchivePath)}";
            statusBrush = MissingBrush;
            actionText = "字体包缺失";
        }

        return new FontPackageCardViewModel
        {
            Package = package,
            StatusText = statusText,
            StatusBrush = statusBrush,
            ActionText = actionText,
            PreviewHint = $"等待游戏内字体效果截图\n{package.Name}.png",
            PreviewImage = LoadPreviewImage(package.PreviewPath),
            CanInstall = archiveExists && !_isLocalizationBusy,
            DeleteVisibility = installed ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private FontPackageCardViewModel CreateTextPackageCard(FontPackage package)
    {
        bool archiveExists = File.Exists(package.ArchivePath);
        bool installed = string.Equals(_settings.InstalledTextPackageId, package.Id, StringComparison.OrdinalIgnoreCase)
            && LocalizationService.CountInstalledTextFiles(_gameRoot) > 0;

        string statusText;
        Brush statusBrush;
        string actionText;
        if (installed)
        {
            statusText = "当前已启用";
            statusBrush = AvailableBrush;
            actionText = "重新安装";
        }
        else if (archiveExists)
        {
            double size = new FileInfo(package.ArchivePath).Length / 1024d / 1024d;
            statusText = $"本地文本包 · {size:0.0} MB";
            statusBrush = new SolidColorBrush(Color.FromRgb(142, 151, 162));
            actionText = "启用文本";
        }
        else
        {
            statusText = $"缺少发行文件：{Path.GetFileName(package.ArchivePath)}";
            statusBrush = MissingBrush;
            actionText = "文本包缺失";
        }

        return new FontPackageCardViewModel
        {
            Package = package,
            StatusText = statusText,
            StatusBrush = statusBrush,
            ActionText = actionText,
            PreviewHint = $"等待游戏内文本效果截图\n{package.Name}.png",
            PreviewImage = LoadPreviewImage(package.PreviewPath),
            CanInstall = archiveExists && !_isLocalizationBusy,
            DeleteVisibility = installed ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private static BitmapSource? LoadPreviewImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private async void InstallFontPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string packageId } ||
            !_fontPackages.TryGetValue(packageId, out FontPackage? package)) return;

        IReadOnlyList<LocalizationContentItem> existingFonts = LocalizationService.DetectExistingFonts(_gameRoot);
        bool cleanExisting = existingFonts.Count > 0;
        if (cleanExisting)
        {
            string paths = string.Join(Environment.NewLine, existingFonts.Select(item => $"• {item.Description}：{item.Path}"));
            MessageBoxResult answer = MessageBox.Show(
                this,
                $"检测到以前安装的字体内容：\n\n{paths}\n\n安装新字体需要先删除这些旧字体，然后把 {package.Name} 安装到游戏根目录。是否继续？",
                "清理旧汉化内容",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                LocalizationStatusDot.Fill = NeutralBrush;
                LocalizationStatusText.Text = "已取消字体安装，现有文件保持不变。";
                return;
            }
        }

        SetLocalizationBusy(true, $"正在安装 {package.Name}，请稍候…");
        try
        {
            FontInstallationResult result = await Task.Run(() =>
                LocalizationService.InstallFontPackage(package, _gameRoot, cleanExisting));

            _settings.InstalledFontPackageId = package.Id;
            _settings.InstalledFontPackageName = package.Name;
            _settings.FontInstalledAtUtc = DateTimeOffset.UtcNow;
            SaveSettings();
            RefreshLocalizationPage();
            SetOperationStatus($"已启用字体：{package.Name}", AvailableBrush);

            MessageBox.Show(
                this,
                $"{package.Name} 安装完成。\n\n已写入 {result.InstalledFileCount} 个 FNT 文件：\n{result.DestinationDirectory}",
                "字体安装完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"字体安装失败：{ex.Message}";
            SetOperationStatus($"字体安装失败：{ex.Message}", MissingBrush);
            MessageBox.Show(this, $"安装 {package.Name} 时发生错误：\n\n{ex.Message}", "字体安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetLocalizationBusy(false, null);
            RefreshLocalizationPage();
        }
    }

    private async void DeleteFontPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string packageId } ||
            !_fontPackages.TryGetValue(packageId, out FontPackage? package)) return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"是否删除当前启用的 {package.Name}？\n\n只会删除游戏根目录下的 settings\\Fonts 文件夹，不会删除 text_ZH 或其他自定义设置。",
            "删除字体",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        SetLocalizationBusy(true, $"正在删除 {package.Name}…");
        try
        {
            bool removed = await Task.Run(() => LocalizationService.RemoveGameFonts(_gameRoot));
            _settings.InstalledFontPackageId = null;
            _settings.InstalledFontPackageName = null;
            _settings.FontInstalledAtUtc = null;
            SaveSettings();
            SetOperationStatus(removed ? $"已删除字体：{package.Name}" : "没有找到已安装的字体文件", removed ? AvailableBrush : NeutralBrush);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"删除字体失败：{ex.Message}";
            MessageBox.Show(this, $"删除字体时发生错误：\n\n{ex.Message}", "删除字体", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetLocalizationBusy(false, null);
            RefreshLocalizationPage();
        }
    }

    private async void InstallTextPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string packageId } ||
            !_textPackages.TryGetValue(packageId, out FontPackage? package)) return;

        IReadOnlyList<LocalizationContentItem> existingTexts = LocalizationService.DetectExistingTexts(_gameRoot);
        bool cleanExisting = existingTexts.Count > 0;
        string paths = existingTexts.Count > 0
            ? string.Join(Environment.NewLine, existingTexts.Select(item => $"• {item.Description}：{item.Path}"))
            : "未检测到旧的汉化文本目录。";

        string attribution = LocalizationService.GetTextPackageAttribution(package);
        MessageBoxResult answer = MessageBox.Show(
            this,
            $"{attribution}\n\n检测结果：\n{paths}\n\n安装 {package.Name} 会把压缩包中的 Text_ZH 内容写入游戏根目录。是否继续？",
            "安装汉化文本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
        {
            LocalizationStatusDot.Fill = NeutralBrush;
            LocalizationStatusText.Text = "已取消汉化文本安装，现有文件保持不变。";
            return;
        }

        SetLocalizationBusy(true, $"正在安装 {package.Name}，请稍候…");
        try
        {
            FontInstallationResult result = await Task.Run(() =>
                LocalizationService.InstallTextPackage(package, _gameRoot, cleanExisting));
            _settings.InstalledTextPackageId = package.Id;
            _settings.InstalledTextPackageName = package.Name;
            _settings.TextInstalledAtUtc = DateTimeOffset.UtcNow;
            SaveSettings();

            RefreshLocalizationPage();
            SetOperationStatus($"已启用汉化文本：{package.Name}", AvailableBrush);

            MessageBox.Show(
                this,
                $"{package.Name} 安装完成。\n\n已写入 {result.InstalledFileCount} 个文本文件：\n{result.DestinationDirectory}",
                "汉化文本安装完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"汉化文本安装失败：{ex.Message}";
            SetOperationStatus($"汉化文本安装失败：{ex.Message}", MissingBrush);
            MessageBox.Show(this, $"安装 {package.Name} 时发生错误：\n\n{ex.Message}", "汉化文本安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetLocalizationBusy(false, null);
            RefreshLocalizationPage();
        }
    }

    private async void DeleteTextPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string packageId } ||
            !_textPackages.TryGetValue(packageId, out FontPackage? package)) return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"是否删除当前启用的 {package.Name}？\n\n只会删除游戏根目录下的 settings\\Text_ZH 文件夹，不会删除 Fonts 或其他自定义设置。",
            "删除汉化文本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        SetLocalizationBusy(true, $"正在删除 {package.Name}…");
        try
        {
            bool removed = await Task.Run(() => LocalizationService.RemoveGameTexts(_gameRoot));
            if (removed)
            {
                _settings.InstalledTextPackageId = null;
                _settings.InstalledTextPackageName = null;
                _settings.TextInstalledAtUtc = null;
                SaveSettings();
            }
            SetOperationStatus(removed ? $"已删除汉化文本：{package.Name}" : "没有找到已安装的汉化文本文件", removed ? AvailableBrush : NeutralBrush);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"删除汉化文本失败：{ex.Message}";
            MessageBox.Show(this, $"删除汉化文本时发生错误：\n\n{ex.Message}", "删除汉化文本", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetLocalizationBusy(false, null);
            RefreshLocalizationPage();
        }
    }

    private void SetLocalizationBusy(bool busy, string? message)
    {
        _isLocalizationBusy = busy;
        FontPackagesItemsControl.ItemsSource = _fontPackages.Values.Select(CreateFontPackageCard).ToArray();
        TextPackagesItemsControl.ItemsSource = _textPackages.Values.Select(CreateTextPackageCard).ToArray();
        RefreshLocalizationButton.IsEnabled = !busy;
        OpenFontPackagesButton.IsEnabled = !busy;
        OpenTextPackagesButton.IsEnabled = !busy;
        OpenGameFontsButton.IsEnabled = !busy;
        OpenGameTextsButton.IsEnabled = !busy;
        OpenUserSettingsButton.IsEnabled = !busy;
        CheckFontUpdatesButton.IsEnabled = !busy && !_fontUpdateCheckInProgress;
        CheckTextUpdatesButton.IsEnabled = !busy && !_fontUpdateCheckInProgress;
        if (!string.IsNullOrWhiteSpace(message))
        {
            LocalizationStatusDot.Fill = WarningBrush;
            LocalizationStatusText.Text = message;
        }
    }

    private void RefreshLocalizationButton_Click(object sender, RoutedEventArgs e) => RefreshLocalizationPage();

    private void OpenFontPackagesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LocalizationService.BundledFontBundleDirectory);
            OpenDirectory(LocalizationService.BundledFontBundleDirectory);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"打开字体包目录失败：{ex.Message}";
        }
    }

    private void OpenTextPackagesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LocalizationService.BundledTextBundleDirectory);
            OpenDirectory(LocalizationService.BundledTextBundleDirectory);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"打开文本包目录失败：{ex.Message}";
        }
    }

    private void OpenGameFontsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string directory = LocalizationService.GetGameFontDirectory(_gameRoot);
            Directory.CreateDirectory(directory);
            OpenDirectory(directory);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"打开游戏字体目录失败：{ex.Message}";
        }
    }

    private void OpenGameTextsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string directory = LocalizationService.GetGameTextDirectory(_gameRoot);
            Directory.CreateDirectory(directory);
            OpenDirectory(directory);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"打开游戏文本目录失败：{ex.Message}";
        }
    }

    private void OpenSourceLink_Click(object sender, RoutedEventArgs e) => OpenUrl(OpenSourceUrl);

    private void OpenUpdateLink_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateUrl);

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo
    {
        FileName = url,
        UseShellExecute = true
    });

    private void OpenUserSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LocalizationService.UserGameSettingsDirectory);
            OpenDirectory(LocalizationService.UserGameSettingsDirectory);
        }
        catch (Exception ex)
        {
            LocalizationStatusDot.Fill = MissingBrush;
            LocalizationStatusText.Text = $"打开本地设置目录失败：{ex.Message}";
        }
    }

    private void ApplyNetworkSettingsToControls()
    {
        _loadingNetworkSettings = true;
        AutoRouteRadioButton.IsChecked = _settings.DownloadRouteMode == DownloadRouteMode.Auto;
        AcceleratedRouteRadioButton.IsChecked = _settings.DownloadRouteMode == DownloadRouteMode.AcceleratedOnly;
        OfficialRouteRadioButton.IsChecked = _settings.DownloadRouteMode == DownloadRouteMode.OfficialOnly;
        SystemProxyRadioButton.IsChecked = _settings.NetworkProxyMode == NetworkProxyMode.System;
        DirectProxyRadioButton.IsChecked = _settings.NetworkProxyMode == NetworkProxyMode.Direct;
        CustomProxyRadioButton.IsChecked = _settings.NetworkProxyMode == NetworkProxyMode.Custom;
        CustomProxyTextBox.Text = _settings.CustomProxyAddress ?? "";
        _loadingNetworkSettings = false;
        UpdateCustomProxyControls();
        RefreshNetworkSettingsSummary();
    }

    private DownloadNetworkOptions GetNetworkOptionsFromControls()
    {
        DownloadRouteMode routeMode = AcceleratedRouteRadioButton.IsChecked == true
            ? DownloadRouteMode.AcceleratedOnly
            : OfficialRouteRadioButton.IsChecked == true
                ? DownloadRouteMode.OfficialOnly
                : DownloadRouteMode.Auto;
        NetworkProxyMode proxyMode = DirectProxyRadioButton.IsChecked == true
            ? NetworkProxyMode.Direct
            : CustomProxyRadioButton.IsChecked == true
                ? NetworkProxyMode.Custom
                : NetworkProxyMode.System;
        string? customAddress = string.IsNullOrWhiteSpace(CustomProxyTextBox.Text)
            ? null
            : CustomProxyTextBox.Text.Trim();
        if (proxyMode == NetworkProxyMode.Custom)
        {
            customAddress = NetworkDownloadService.NormalizeCustomProxyAddress(customAddress).AbsoluteUri.TrimEnd('/');
        }
        return new DownloadNetworkOptions(routeMode, proxyMode, customAddress);
    }

    private void NetworkSetting_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingNetworkSettings || !IsLoaded) return;
        UpdateCustomProxyControls();
        NetworkSettingsStatusText.Text = "设置已更改，点击保存后生效。";
        NetworkSettingsStatusDot.Fill = NeutralBrush;
        try
        {
            NetworkRouteSummaryText.Text = NetworkDownloadService.GetRouteSummary(GetNetworkOptionsFromControls());
        }
        catch
        {
            NetworkRouteSummaryText.Text = "请完善自定义代理地址后保存。";
        }
    }

    private void CustomProxyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingNetworkSettings || !IsLoaded) return;
        NetworkSettingsStatusText.Text = "代理地址已更改，点击测试连接或保存。";
        NetworkSettingsStatusDot.Fill = NeutralBrush;
    }

    private void UpdateCustomProxyControls()
    {
        bool enabled = CustomProxyRadioButton.IsChecked == true;
        CustomProxyTextBox.IsEnabled = enabled;
    }

    private void RefreshNetworkSettingsSummary()
    {
        DownloadNetworkOptions options = DownloadNetworkOptions.FromSettings(_settings);
        NetworkRouteSummaryText.Text = NetworkDownloadService.GetRouteSummary(options);
        LocalizationNetworkText.Text = $"更新线路：{NetworkDownloadService.GetRouteSummary(options)}";
        SystemProxyInfo proxy = NetworkDownloadService.GetSystemProxyInfo();
        SystemProxyStatusText.Text = proxy.IsEnabled && !string.IsNullOrWhiteSpace(proxy.Address)
            ? $"已检测到 Windows 系统代理：{proxy.Address}"
            : "当前未启用 Windows 系统代理";
        SystemProxyStatusText.Foreground = proxy.IsEnabled ? AvailableBrush : NeutralBrush;
    }

    private void SaveNetworkSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DownloadNetworkOptions options = GetNetworkOptionsFromControls();
            _settings.DownloadRouteMode = options.RouteMode;
            _settings.NetworkProxyMode = options.ProxyMode;
            _settings.CustomProxyAddress = options.CustomProxyAddress;
            SettingsService.Save(_settings);
            RefreshNetworkSettingsSummary();
            NetworkSettingsStatusDot.Fill = AvailableBrush;
            NetworkSettingsStatusText.Text = "网络设置已保存，后续更新请求将使用新线路。";
            SetOperationStatus("网络设置已保存", AvailableBrush);
        }
        catch (Exception ex)
        {
            NetworkSettingsStatusDot.Fill = MissingBrush;
            NetworkSettingsStatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "保存网络设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void TestNetworkConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_networkTestInProgress) return;
        try
        {
            DownloadNetworkOptions options = GetNetworkOptionsFromControls();
            _networkTestInProgress = true;
            TestNetworkConnectionButton.IsEnabled = false;
            SaveNetworkSettingsButton.IsEnabled = false;
            NetworkSettingsStatusDot.Fill = NeutralBrush;
            NetworkSettingsStatusText.Text = "正在测试 GitHub 官方清单连接…";
            NetworkConnectionTestResult result = await NetworkDownloadService.TestOfficialConnectionAsync(
                FontUpdateService.ManifestUrl,
                options);
            NetworkSettingsStatusDot.Fill = result.Success ? AvailableBrush : MissingBrush;
            NetworkSettingsStatusText.Text = result.Success
                ? $"{result.RouteName}：{result.Message}"
                : $"{result.RouteName}测试失败：{result.Message}";
        }
        catch (Exception ex)
        {
            NetworkSettingsStatusDot.Fill = MissingBrush;
            NetworkSettingsStatusText.Text = ex.Message;
        }
        finally
        {
            _networkTestInProgress = false;
            TestNetworkConnectionButton.IsEnabled = true;
            SaveNetworkSettingsButton.IsEnabled = true;
        }
    }

private void SaveSettings()
    {
        _settings.GameRoot = _gameRoot;
        _settings.CloseAfterLaunch = CloseAfterLaunchCheckBox.IsChecked == true;
        _settings.LastPage = _currentPage;
        try
        {
            SettingsService.Save(_settings);
        }
        catch
        {
            // 本次操作仍可继续，退出时会再次尝试保存。
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e) => SaveSettings();

    private void CreateDesktopShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DesktopShortcutResult result = DesktopShortcutService.CreateOrReplace();
            string message = result.ReplacedExisting
                ? "桌面快捷方式已更新"
                : "桌面快捷方式已创建";
            SetOperationStatus(message, AvailableBrush);
            MessageBox.Show(
                this,
                $"{message}：\n\n{result.ShortcutPath}\n\n以后可以直接从桌面打开 Grim Dawn Runner Lite。",
                "桌面快捷方式",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetOperationStatus($"创建桌面快捷方式失败：{ex.Message}", MissingBrush);
            MessageBox.Show(this, $"创建桌面快捷方式时发生错误：\n\n{ex.Message}", "桌面快捷方式", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(SaveDirectory); OpenDirectory(SaveDirectory); SetOperationStatus("已打开本地存档目录", AvailableBrush); }
        catch (Exception ex) { SetOperationStatus($"打开存档目录失败：{ex.Message}", MissingBrush); }
    }
    private void OpenRootButton_Click(object sender, RoutedEventArgs e)
    {
        try { OpenDirectory(_gameRoot); SetOperationStatus("已打开游戏根目录", AvailableBrush); }
        catch (Exception ex) { SetOperationStatus($"打开根目录失败：{ex.Message}", MissingBrush); }
    }
    private void CopyRootButton_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_gameRoot); SetOperationStatus("根目录路径已复制到剪贴板", AvailableBrush); }
        catch (Exception ex) { SetOperationStatus($"复制路径失败：{ex.Message}", MissingBrush); }
    }
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAvailability();
    private static void OpenDirectory(string directory) => Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = QuoteArgument(directory), UseShellExecute = true });
    private void SetOperationStatus(string message, Brush? brush = null) { OperationStatusText.Text = message; OperationStatusDot.Fill = brush ?? NeutralBrush; }
    private static string CombineArguments(string first, string rest) => string.IsNullOrWhiteSpace(rest) ? first : first + " " + rest;
    private static string BuildForwardedArguments(IEnumerable<string> args) => string.Join(" ", args.Select(QuoteArgument));

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument)) return "\"\"";
        if (!argument.Any(char.IsWhiteSpace) && !argument.Contains('"')) return argument;
        var builder = new StringBuilder(); builder.Append('"'); int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"') { builder.Append('\\', backslashes * 2 + 1); builder.Append('"'); backslashes = 0; continue; }
            builder.Append('\\', backslashes); backslashes = 0; builder.Append(character);
        }
        builder.Append('\\', backslashes * 2); builder.Append('"'); return builder.ToString();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeGlyph is null) return;
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        WindowBorder.BorderThickness = new Thickness(0);
    }
}
