using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf_gdRunnerLite.Models;
using Wpf_gdRunnerLite.Services;

namespace Wpf_gdRunnerLite;

public partial class FontUpdateDialog : Window
{
    private readonly LauncherSettings _settings;
    private readonly List<FontUpdateItemViewModel> _items;
    private bool _downloadInProgress;

    public bool PackagesChanged { get; private set; }

    public FontUpdateDialog(IReadOnlyList<FontPackageUpdate> updates, LauncherSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _items = updates.Select(update => new FontUpdateItemViewModel(update)).ToList();
        DataContext = _items;
        SummaryText.Text = $"发现 {_items.Count} 个可下载项目。下载完成后会校验 SHA-256，并保存到程序目录的 Assets\\FontBundle。";
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadInProgress || sender is not Button { Tag: string packageId }) return;
        FontUpdateItemViewModel? item = _items.FirstOrDefault(candidate =>
            string.Equals(candidate.Package.Id, packageId, StringComparison.OrdinalIgnoreCase));
        if (item is null || item.Completed) return;

        _downloadInProgress = true;
        SetWindowActionsEnabled(false);
        foreach (FontUpdateItemViewModel candidate in _items) candidate.CanDownload = false;
        item.ProgressVisibility = Visibility.Visible;
        item.ButtonText = "下载中";
        FooterStatusText.Text = $"正在下载 {item.Package.Name}…";

        var progress = new Progress<FontDownloadProgress>(value =>
        {
            item.ProgressValue = value.Percentage;
            item.PercentageText = $"{value.Percentage:0}%";
            item.ProgressText = FormatProgress(value);
        });

        try
        {
            FontPackageDownloadResult result = await FontUpdateService.DownloadPackageAsync(item.Package, progress);
            _settings.FontPackageStates[item.Package.Id] = new FontPackageState
            {
                Version = result.Version,
                Sha256 = result.Sha256,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            SettingsService.Save(_settings);

            item.Completed = true;
            item.ButtonText = "已下载";
            item.ProgressValue = 100;
            item.PercentageText = "100%";
            item.ProgressText = "下载和校验完成";
            PackagesChanged = true;
            FooterStatusText.Text = $"{item.Package.Name} 已下载，可以继续下载其他项目。";

            if (_items.All(candidate => candidate.Completed))
            {
                SummaryText.Text = "全部字体更新已经下载完成，关闭窗口后商品列表会自动刷新。";
                FooterStatusText.Text = "全部下载完成。";
            }
        }
        catch (Exception ex)
        {
            item.ButtonText = "重新下载";
            item.ProgressText = $"下载失败：{ex.Message}";
            item.PercentageText = "失败";
            FooterStatusText.Text = $"{item.Package.Name} 下载失败。";
            MessageBox.Show(this, $"下载 {item.Package.Name} 时发生错误：\n\n{ex.Message}", "字体下载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _downloadInProgress = false;
            SetWindowActionsEnabled(true);
            foreach (FontUpdateItemViewModel candidate in _items)
            {
                candidate.CanDownload = !candidate.Completed;
            }
        }
    }

    private void SetWindowActionsEnabled(bool enabled)
    {
        TitleCloseButton.IsEnabled = enabled;
        FooterCloseButton.IsEnabled = enabled;
    }

    private static string FormatProgress(FontDownloadProgress progress)
    {
        if (progress.TotalBytes is > 0)
        {
            return $"{progress.Stage} · {FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes.Value)}";
        }
        return $"{progress.Stage} · {FormatBytes(progress.BytesReceived)}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.0} {units[unit]}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_downloadInProgress) Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private sealed class FontUpdateItemViewModel : INotifyPropertyChanged
    {
        private bool _canDownload = true;
        private bool _completed;
        private string _buttonText = "下载";
        private Visibility _progressVisibility = Visibility.Collapsed;
        private string _progressText = "等待下载";
        private string _percentageText = "0%";
        private double _progressValue;

        public FontUpdateItemViewModel(FontPackageUpdate update)
        {
            Update = update;
            DifferenceText = update.IsMissing
                ? "本地尚未下载"
                : update.VersionChanged && update.HashChanged
                    ? "远端版本和字体包哈希均有变化"
                    : update.VersionChanged
                        ? "远端版本号有变化"
                        : update.HashChanged
                            ? "本地字体包哈希与远端不一致"
                            : "本地预览图缺失或与远端不一致";
        }

        public FontPackageUpdate Update { get; }
        public RemoteFontPackage Package => Update.Package;
        public string VersionLabel => "v" + Package.Version;
        public string DifferenceText { get; }

        public bool CanDownload
        {
            get => _canDownload;
            set => SetField(ref _canDownload, value);
        }

        public bool Completed
        {
            get => _completed;
            set => SetField(ref _completed, value);
        }

        public string ButtonText
        {
            get => _buttonText;
            set => SetField(ref _buttonText, value);
        }

        public Visibility ProgressVisibility
        {
            get => _progressVisibility;
            set => SetField(ref _progressVisibility, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetField(ref _progressText, value);
        }

        public string PercentageText
        {
            get => _percentageText;
            set => SetField(ref _percentageText, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetField(ref _progressValue, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
