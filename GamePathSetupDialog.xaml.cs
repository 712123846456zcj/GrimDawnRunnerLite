using System.Windows;
using System.Windows.Input;
using Wpf_gdRunnerLite.Services;

namespace Wpf_gdRunnerLite;

public partial class GamePathSetupDialog : Window
{
    public string? SelectedGameRoot { get; private set; }

    public GamePathSetupDialog()
    {
        InitializeComponent();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SelectExecutableButton_Click(object sender, RoutedEventArgs e)
    {
        string? gameRoot = GameLocationService.SelectGameRoot(this);
        if (gameRoot is null)
        {
            SetupStatusText.Text = "未选择有效的 Grim Dawn.exe。请确认文件位于游戏根目录、x64 或 x86 文件夹中。";
            return;
        }

        SelectedGameRoot = gameRoot;
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
