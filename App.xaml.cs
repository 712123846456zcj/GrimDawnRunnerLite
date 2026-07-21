using System.IO;
using System.Windows;
using Wpf_gdRunnerLite.Models;
using Wpf_gdRunnerLite.Services;

namespace Wpf_gdRunnerLite;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        LauncherSettings settings = SettingsService.Load();
        string launcherDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string? gameRoot = GameLocationService.ResolveRootFromDirectory(launcherDirectory)
            ?? GameLocationService.ResolveRootFromDirectory(settings.GameRoot);

        if (gameRoot is null)
        {
            var setupDialog = new GamePathSetupDialog();
            bool? setupResult = setupDialog.ShowDialog();
            if (setupResult != true || setupDialog.SelectedGameRoot is null)
            {
                Shutdown();
                return;
            }

            gameRoot = setupDialog.SelectedGameRoot;
        }

        settings.GameRoot = gameRoot;
        try
        {
            SettingsService.Save(settings);
        }
        catch
        {
            // 设置写入失败不影响本次启动。
        }

        MainWindow = new MainWindow(gameRoot, settings);
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }
}
